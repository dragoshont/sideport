using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sideport.Api.Operations;

namespace Sideport.Api.Tests;

public partial class ApiSmokeTests
{
    [Fact]
    public async Task MetricsSnapshot_ReturnsOutcomeFocusedAggregateData()
    {
        using var factory = Factory(apiToken: "metrics-test-token", operationWorker: false);
        _ = factory.CreateClient();

        string body = await factory.Services.GetRequiredService<SideportOperationalMetrics>()
            .CollectAsync();

        Assert.Contains("sideport_scheduler_enabled 0", body, StringComparison.Ordinal);
        Assert.Contains("sideport_registered_apps 0", body, StringComparison.Ordinal);
        Assert.Contains("sideport_unknown_device_operations 0", body, StringComparison.Ordinal);
        Assert.Contains("sideport_grandslam_http_responses_total", body, StringComparison.Ordinal);
        Assert.DoesNotContain("metrics-test-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-DEVICE-UUID", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_HistoricalReconciledOperationDoesNotHoldQuarantine()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Loopback);
        using HttpClient client = factory.CreateClient();
        OperationStore store = factory.Services.GetRequiredService<OperationStore>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var target = new Sideport.Api.Operations.OperationTargetDto(
            "TEST-UDID",
            "com.example.app",
            TeamId: "TEAM",
            Kind: "app",
            AccountProfileId: "profile",
            Version: "1.0.0",
            CatalogSha256: new string('a', 64));
        Sideport.Api.Operations.OperationRecordDto historicalUnknown =
            Operation("historical", "refresh", "unknown", target);
        Sideport.Api.Operations.OperationRecordDto reconciliation = Operation(
            "reconciliation",
            "reconcile",
            "succeeded",
            target,
            parentOperationId: historicalUnknown.OperationId,
            stages: [new("verify", "Verify", "succeeded", now, now, "Verified.")],
            result: new(
                Success: false,
                BundleId: target.BundleId,
                ExpiresAt: null,
                Error: null,
                SafeToRerun: true,
                ReconciledOperationId: historicalUnknown.OperationId));
        Sideport.Api.Operations.OperationRecordDto activeUnknown =
            Operation("active", "refresh", "recovery-required", target);

        await store.AddIfIdempotentMissingAsync(historicalUnknown);
        await store.AddIfIdempotentMissingAsync(reconciliation);

        string reconciledBody = await client.GetStringAsync("/metrics");
        Assert.Contains("sideport_unknown_device_operations 0", reconciledBody, StringComparison.Ordinal);
        Assert.Contains("sideport_scheduler_lock_held 0", reconciledBody, StringComparison.Ordinal);

        await store.AddIfIdempotentMissingAsync(activeUnknown);

        string unresolvedBody = await client.GetStringAsync("/metrics");
        Assert.Contains("sideport_unknown_device_operations 1", unresolvedBody, StringComparison.Ordinal);
        Assert.Contains("sideport_scheduler_lock_held 1", unresolvedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_LoopbackRequestReturnsPrometheusPayload()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Loopback);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/metrics");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0.0.4", response.Content.Headers.ContentType?.Parameters
            .Single(parameter => parameter.Name == "version").Value);
        Assert.Contains("sideport_scheduler_enabled 0", body, StringComparison.Ordinal);
        Assert.Contains("sideport_registered_apps 0", body, StringComparison.Ordinal);
        Assert.Contains("sideport_grandslam_http_responses_total", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_ConfiguredNetworkRequestReturnsPrometheusPayload()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Parse("10.1.2.3"),
            metricsAllowedNetworks: "10.1.0.0/16");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "sideport_scheduler_enabled",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_UnconfiguredNonLoopbackRequestFailsClosed()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Parse("192.0.2.10"));
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics")).StatusCode);
    }

    [Fact]
    public async Task Metrics_TrustedProxyUsesForwardedExternalClientAndFailsClosed()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Parse("10.1.2.3"),
            metricsAllowedNetworks: "10.1.0.0/16",
            knownProxies: "10.1.2.3");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics")).StatusCode);
    }

    [Fact]
    public async Task Metrics_UntrustedProxyCannotSpoofLoopbackClient()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            remoteIp: IPAddress.Parse("192.0.2.10"),
            knownProxies: "10.1.2.3");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/metrics")).StatusCode);
    }

    [Fact]
    public void Metrics_InvalidAllowedNetworkFailsStartup()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            operationWorker: false,
            metricsAllowedNetworks: "not-a-cidr");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => factory.CreateClient());

        Assert.Contains(
            "Sideport:Metrics:AllowedNetworks contains invalid CIDR network",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsAccessPolicy_AllowsOnlyLoopbackOrConfiguredNetworks()
    {
        System.Net.IPNetwork[] networks =
        [
            System.Net.IPNetwork.Parse("10.1.0.0/16"),
        ];

        Assert.True(MetricsAccessPolicy.IsAllowed(IPAddress.Loopback, networks));
        Assert.True(MetricsAccessPolicy.IsAllowed(IPAddress.Parse("::ffff:10.1.2.3"), networks));
        Assert.False(MetricsAccessPolicy.IsAllowed(IPAddress.Parse("192.0.2.10"), networks));
        Assert.False(MetricsAccessPolicy.IsAllowed(null, networks));
    }

    [Fact]
    public async Task OperationalSnapshot_DoesNotExposeResourceIdentifiers()
    {
        using var factory = Factory(
            apiToken: "metrics-test-token",
            personalAppleId: "sensitive-person@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false);
        _ = factory.CreateClient();

        string body = await factory.Services.GetRequiredService<SideportOperationalMetrics>()
            .CollectAsync();

        Assert.DoesNotContain("sensitive-person", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configured-host-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("accountProfileId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceUdid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("bundleId", body, StringComparison.Ordinal);
    }

    private static Sideport.Api.Operations.OperationRecordDto Operation(
        string operationId,
        string type,
        string status,
        Sideport.Api.Operations.OperationTargetDto target,
        string? parentOperationId = null,
        IReadOnlyList<Sideport.Api.Operations.OperationStageDto>? stages = null,
        Sideport.Api.Operations.OperationResultDto? result = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new Sideport.Api.Operations.OperationRecordDto(
            operationId,
            type,
            status,
            now,
            StartedAt: null,
            now,
            CompletedAt: null,
            new Sideport.Api.Operations.OperationActorDto("test", "metrics-test"),
            IdempotencyKey: operationId,
            Attempt: 1,
            target,
            Stages: stages ?? [],
            Result: result,
            Error: null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId,
            ParentOperationId: parentOperationId);
    }
}
