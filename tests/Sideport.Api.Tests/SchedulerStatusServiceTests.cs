using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class SchedulerStatusServiceTests
{
    [Fact]
    public async Task Enable_DoesNotCombineSignerAndDeviceReadinessAcrossRegistrations()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "sideport-scheduler-status-tests",
            Guid.NewGuid().ToString("N"));
        var settings = new SchedulerSettingsStore(Path.Combine(root, "scheduler.json"));
        await settings.InitializeAsync(requestedEnabled: false, prerequisitesSatisfied: false);
        var registry = new InMemoryAppRegistry();
        var operations = new OperationStore(Path.Combine(root, "operations.json"));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await AddVerifiedRegistrationAsync(
            registry,
            operations,
            "UDID-SIGNER-ONLY",
            "com.example.signer",
            "signer@example.com",
            "op_signer",
            now);
        await AddVerifiedRegistrationAsync(
            registry,
            operations,
            "UDID-DEVICE-ONLY",
            "com.example.device",
            "missing@example.com",
            "op_device",
            now);

        var service = new SchedulerStatusService(
            settings,
            registry,
            operations,
            new SplitSigningIdentityProvider(),
            new OneReachableDeviceController(),
            new OrchestratorOptions());

        SchedulerSettingsUpdateResult result = await service.SetEnabledAsync(true);

        Assert.Equal("scheduler-prerequisites-not-met", result.Error);
        Assert.Contains("No single verified app registration", result.Message, StringComparison.Ordinal);
        Assert.False((await settings.ReadAsync())!.Enabled);
    }

    private static async Task AddVerifiedRegistrationAsync(
        IAppRegistry registry,
        OperationStore operations,
        string udid,
        string bundleId,
        string appleId,
        string operationId,
        DateTimeOffset now)
    {
        await registry.UpsertAsync(new AppRegistration(
            bundleId,
            appleId,
            "TEAMID1234",
            udid,
            $"/tmp/{bundleId}.ipa",
            Lifecycle: "active",
            LastVerifiedOperationId: operationId));
        await operations.AddIfIdempotentMissingAsync(new OperationRecordDto(
            operationId,
            "verify-existing-registration",
            "succeeded",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            now.AddMinutes(-1),
            new OperationActorDto("api-token", "api-token-client"),
            operationId,
            Attempt: 1,
            new OperationTargetDto(udid, bundleId, TeamId: "TEAMID1234", Kind: "app"),
            [new OperationStageDto(
                "verify", "Verify", "succeeded", now.AddMinutes(-2), now.AddMinutes(-1), "Verified.")],
            new OperationResultDto(true, bundleId, now.AddDays(5), null, Version: "1.0"),
            Error: null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId));
    }

    private sealed class SplitSigningIdentityProvider : ISigningIdentityProvider
    {
        public Task<SigningIdentityInspection> InspectAsync(
            string appleId,
            string teamId,
            CancellationToken ct = default) =>
            Task.FromResult(string.Equals(appleId, "signer@example.com", StringComparison.Ordinal)
                ? new SigningIdentityInspection("reusable", DateTimeOffset.UtcNow.AddMonths(1), "TEST")
                : new SigningIdentityInspection("missing", null, null));

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            CancellationToken ct = default) =>
            Task.FromException<PreparedSigningInputs>(new NotSupportedException());
    }

    private sealed class OneReachableDeviceController : IDeviceController
    {
        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                "UDID-DEVICE-ONLY",
                "iPhone",
                "iPhone17,1",
                "26.0",
                DeviceConnection.Usb,
                "trusted",
                "Trusted over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true)]);

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string udid,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }
}
