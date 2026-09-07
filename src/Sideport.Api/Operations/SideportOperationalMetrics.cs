using System.Globalization;
using System.Net;
using System.Text;
using Sideport.Api.AppleAccess;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.Devices;

namespace Sideport.Api.Operations;

public sealed class SideportOperationalMetrics(
    SchedulerStatusService scheduler,
    OperationService operations,
    OperationStore operationStore,
    IPersonalAppleAccess appleAccess,
    DeviceMetrics deviceMetrics,
    GrandSlamMetrics grandSlamMetrics,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<string> CollectAsync(CancellationToken ct = default)
    {
        SchedulerStatusDto schedulerStatus = await scheduler.GetAsync(ct).ConfigureAwait(false);
        IReadOnlyList<RenewalItemDto> renewals = await operations.RenewalsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> operationRecords =
            await operationStore.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        PersonalAppleStatusDto apple = await appleAccess.StatusAsync(ct).ConfigureAwait(false);
        DateTimeOffset now = _time.GetUtcNow();

        int unknownOperations = operationRecords.Count(record =>
            record.Type is "install" or "refresh" &&
            record.Status is "unknown" or "recovery-required");
        int expiredProfiles = renewals.Count(item => item.ExpiresAt is { } expiry && expiry <= now);
        int blockedRenewals = renewals.Count(item =>
            string.Equals(item.Status, "blocked", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(item.Blocker));
        DateTimeOffset? minimumExpiry = renewals.Where(item => item.ExpiresAt is not null)
            .Select(item => item.ExpiresAt!.Value)
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
        DateTimeOffset? lastVerifiedRenewal = operationRecords
            .Where(record =>
                record.Type == "refresh" &&
                record.Status == "succeeded" &&
                record.Result?.Success == true &&
                record.CompletedAt is not null)
            .MaxBy(record => record.CompletedAt)
            ?.CompletedAt;

        var text = new StringBuilder();
        Gauge(text, "sideport_scheduler_enabled", "Whether durable automatic refresh is enabled.", schedulerStatus.Enabled);
        Gauge(text, "sideport_scheduler_lock_held", "Whether unresolved device mutation evidence holds the scheduler.", schedulerStatus.Concurrency.LockState == "held");
        Gauge(text, "sideport_scheduler_busy", "Whether a device mutation is currently active.", schedulerStatus.Concurrency.LockState == "busy");
        Gauge(text, "sideport_registered_apps", "Registered applications projected by renewal state.", renewals.Count);
        Gauge(text, "sideport_due_apps", "Applications currently due for refresh.", schedulerStatus.DueCount);
        Gauge(text, "sideport_queued_refresh_operations", "Refresh operations waiting for execution.", schedulerStatus.QueuedCount);
        Gauge(text, "sideport_unknown_device_operations", "Install or refresh operations requiring reconciliation.", unknownOperations);
        Gauge(text, "sideport_expired_profiles", "Registered applications whose last verified signing profile has expired.", expiredProfiles);
        Gauge(text, "sideport_blocked_renewals", "Registered applications with a current renewal blocker.", blockedRenewals);
        Gauge(text, "sideport_next_scheduler_evaluation_timestamp_seconds", "Next scheduled evaluation as a Unix timestamp.", schedulerStatus.NextEvaluationAt);
        Gauge(text, "sideport_last_scheduler_evaluation_timestamp_seconds", "Last completed scheduler evaluation as a Unix timestamp.", schedulerStatus.LastEvaluation?.CompletedAt);
        Gauge(text, "sideport_last_verified_renewal_timestamp_seconds", "Last device-verified refresh completion as a Unix timestamp.", lastVerifiedRenewal);
        Gauge(text, "sideport_minimum_profile_expiry_timestamp_seconds", "Earliest known registered-app profile expiry as a Unix timestamp.", minimumExpiry);
        text.AppendLine("# HELP sideport_apple_auth_state Current aggregate Personal Apple authentication state.");
        text.AppendLine("# TYPE sideport_apple_auth_state gauge");
        text.Append("sideport_apple_auth_state{state=\"")
            .Append(NormalizeAppleState(apple.State))
            .AppendLine("\"} 1");
        Gauge(text, "sideport_metrics_snapshot_timestamp_seconds", "Time this internally consistent metrics snapshot completed.", now);
        text.Append(deviceMetrics.ToPrometheusText());
        text.Append(grandSlamMetrics.ToPrometheusText());
        return text.ToString();
    }

    private static void Gauge(StringBuilder text, string name, string help, bool value) =>
        Gauge(text, name, help, value ? 1 : 0);

    private static void Gauge(StringBuilder text, string name, string help, int value)
    {
        text.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        text.Append("# TYPE ").Append(name).AppendLine(" gauge");
        text.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Gauge(StringBuilder text, string name, string help, DateTimeOffset? value)
    {
        text.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        text.Append("# TYPE ").Append(name).AppendLine(" gauge");
        text.Append(name).Append(' ')
            .AppendLine((value?.ToUnixTimeSeconds() ?? 0).ToString(CultureInfo.InvariantCulture));
    }

    private static string NormalizeAppleState(string state) => state switch
    {
        "validated-recently" => "validated-recently",
        "validation-stale" => "validation-stale",
        "credential-configured" => "credential-configured",
        "two-factor-required" => "two-factor-required",
        "not-configured" => "not-configured",
        "failed" => "failed",
        _ => "unknown",
    };
}

internal static class MetricsAccessPolicy
{
    public static bool IsAllowed(IPAddress? remoteAddress, IReadOnlyList<System.Net.IPNetwork> allowedNetworks)
    {
        if (remoteAddress is null)
            return false;
        IPAddress address = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;
        return IPAddress.IsLoopback(address) ||
            allowedNetworks.Any(network => network.Contains(address));
    }
}
