using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sideport.Core;
using Sideport.Orchestrator.Tests.Support;

namespace Sideport.Orchestrator.Tests;

public class RefreshSchedulerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _inputIpa;
    private readonly InMemoryAppRegistry _registry = new();
    private readonly FakePortal _portal = new();
    private readonly FakeCredentialProvider _credentials = new();
    private readonly FakeSigningIdentityProvider _identity = new();
    private readonly FakeSigner _signer = new();
    private readonly FakeDeviceController _devices = new();
    private readonly OrchestratorOptions _options;

    public RefreshSchedulerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sideport-sched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _inputIpa = Path.Combine(_dir, "in.ipa");
        File.WriteAllBytes(_inputIpa, [1]);
        _options = new OrchestratorOptions
        {
            WorkDirectory = Path.Combine(_dir, "signed"),
            RefreshLeadTime = TimeSpan.FromDays(2),
        };
        _credentials.Add("me@example.com", "pw");
    }

    private RefreshOrchestrator BuildOrchestrator()
    {
        var sessions = new SessionManager(_portal, _credentials, NullLogger<SessionManager>.Instance);
        return new RefreshOrchestrator(
            _registry, sessions, _identity, _signer, _devices, _options,
            NullLogger<RefreshOrchestrator>.Instance);
    }

    private async Task RegisterAsync(string bundleId, string udid = "UDID-1")
    {
        await _registry.UpsertAsync(new AppRegistration(bundleId, "me@example.com", "TEAM", udid, _inputIpa));
    }

    [Fact]
    public async Task RunOnce_NeverSignedApp_IsRefreshed()
    {
        await RegisterAsync("com.example.app");
        RefreshOrchestrator orchestrator = BuildOrchestrator();
        var scheduler = new RefreshScheduler(_registry, orchestrator, _options, TimeProvider.System,
            NullLogger<RefreshScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, _signer.SignCalls);
    }

    [Fact]
    public async Task RunOnce_FreshlySignedApp_IsNotRefreshedAgain()
    {
        await RegisterAsync("com.example.app");
        _identity.Expiry = DateTimeOffset.UtcNow.AddDays(7); // well beyond the 2-day lead
        RefreshOrchestrator orchestrator = BuildOrchestrator();
        var scheduler = new RefreshScheduler(_registry, orchestrator, _options, TimeProvider.System,
            NullLogger<RefreshScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None); // first sign
        await scheduler.RunOnceAsync(CancellationToken.None); // should skip

        Assert.Equal(1, _signer.SignCalls);
    }

    [Fact]
    public async Task RunOnce_AppNearingExpiry_IsRefreshed()
    {
        await RegisterAsync("com.example.app");
        _identity.Expiry = DateTimeOffset.UtcNow.AddHours(12); // within the 2-day lead
        RefreshOrchestrator orchestrator = BuildOrchestrator();
        var scheduler = new RefreshScheduler(_registry, orchestrator, _options, TimeProvider.System,
            NullLogger<RefreshScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None); // sign #1, expiry 12h out
        await scheduler.RunOnceAsync(CancellationToken.None); // still due -> sign #2

        Assert.Equal(2, _signer.SignCalls);
    }

    [Fact]
    public async Task RunOnce_OneAppFails_DoesNotStopOthers()
    {
        await RegisterAsync("com.example.good", "UDID-GOOD");
        await RegisterAsync("com.example.bad", "UDID-BAD");
        // The bad app has a missing input IPA → its refresh fails, but the good
        // one must still be processed.
        await _registry.UpsertAsync(new AppRegistration(
            "com.example.bad", "me@example.com", "TEAM", "UDID-BAD",
            Path.Combine(_dir, "missing.ipa")));

        RefreshOrchestrator orchestrator = BuildOrchestrator();
        var scheduler = new RefreshScheduler(_registry, orchestrator, _options, TimeProvider.System,
            NullLogger<RefreshScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Single(_devices.Installs);
        Assert.Equal("UDID-GOOD", _devices.Installs[0].Udid);
    }

    [Fact]
    public async Task ExecuteAsync_FiresOnSchedule_WithFakeTime()
    {
        await RegisterAsync("com.example.app");
        _options.ScheduleInterval = TimeSpan.FromHours(1);
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        RefreshOrchestrator orchestrator = BuildOrchestrator();
        var scheduler = new RefreshScheduler(_registry, orchestrator, _options, fakeTime,
            NullLogger<RefreshScheduler>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        // First tick runs immediately on start.
        await WaitForAsync(() => _signer.SignCalls >= 1);

        // Advance one interval → another evaluation (app now signed far out → no new sign).
        fakeTime.Advance(TimeSpan.FromHours(1));
        await Task.Delay(50);

        await scheduler.StopAsync(CancellationToken.None);
        Assert.True(_signer.SignCalls >= 1);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        int waited = 0;
        while (!condition() && waited < timeoutMs)
        {
            await Task.Delay(25);
            waited += 25;
        }
        Assert.True(condition(), "condition not met within timeout");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
