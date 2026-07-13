using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.Orchestrator.Tests.Support;

namespace Sideport.Orchestrator.Tests;

public class RefreshOrchestratorTests : IDisposable
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

    public RefreshOrchestratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sideport-orch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _inputIpa = Path.Combine(_dir, "in.ipa");
        File.WriteAllBytes(_inputIpa, [1, 2, 3]);
        _options = new OrchestratorOptions { WorkDirectory = Path.Combine(_dir, "signed") };
        _identity.Pkcs12Path = Path.Combine(_dir, "prepared", "identity.p12");
        _identity.ProfilePath = Path.Combine(_dir, "prepared", "profile.mobileprovision");
        _credentials.Add("me@example.com", "pw");
    }

    private RefreshOrchestrator Build()
    {
        var sessions = new SessionManager(_portal, _credentials, NullLogger<SessionManager>.Instance);
        return new RefreshOrchestrator(
            _registry, sessions, _identity, _signer, _devices, _options,
            NullLogger<RefreshOrchestrator>.Instance);
    }

    private async Task RegisterAsync(string bundleId = "com.example.app", string udid = "UDID-1")
    {
        await _registry.UpsertAsync(new AppRegistration(
            bundleId, "me@example.com", "TEAM", udid, _inputIpa));
    }

    [Fact]
    public async Task RefreshAsync_HappyPath_SignsInstallsAndRecordsExpiry()
    {
        await RegisterAsync();
        _identity.Expiry = DateTimeOffset.UtcNow.AddDays(7);
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.True(result.Success, result.Error);
        Assert.Equal("com.example.app", result.BundleId);
        Assert.Equal(_identity.Expiry, result.NewExpiry);
        Assert.Single(_devices.Installs);
        Assert.Equal("UDID-1", _devices.Installs[0].Udid);

        RefreshState? state = orchestrator.GetState("UDID-1", "com.example.app");
        Assert.NotNull(state);
        Assert.True(state!.LastSucceeded);
    }

    [Fact]
    public async Task RefreshAsync_RemovesTransientSigningInputsAfterUse()
    {
        await RegisterAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(_identity.Pkcs12Path)!);
        await File.WriteAllBytesAsync(_identity.Pkcs12Path, [1, 2, 3]);
        await File.WriteAllBytesAsync(_identity.ProfilePath, [4, 5, 6]);
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.True(result.Success, result.Error);
        Assert.False(File.Exists(_identity.Pkcs12Path));
        Assert.False(File.Exists(_identity.ProfilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(_identity.Pkcs12Path)!));
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentRequests_NeverSignConcurrently()
    {
        // Register several apps and fire them all at once; the single-signer rule
        // (invariant #5) must serialize every re-sign through one lock.
        for (int i = 0; i < 6; i++)
            await RegisterAsync($"com.example.app{i}", $"UDID-{i}");

        _signer.Delay = TimeSpan.FromMilliseconds(40);
        RefreshOrchestrator orchestrator = Build();

        Task<RefreshResult>[] tasks =
        [
            .. Enumerable.Range(0, 6).Select(i =>
                orchestrator.RefreshAsync($"UDID-{i}", $"com.example.app{i}")),
        ];
        await Task.WhenAll(tasks);

        Assert.All(tasks, t => Assert.True(t.Result.Success));
        Assert.Equal(6, _signer.SignCalls);
        Assert.Equal(1, _signer.MaxObservedConcurrency); // never two signs at once
    }

    [Fact]
    public async Task RefreshAsync_UnregisteredApp_Fails()
    {
        RefreshOrchestrator orchestrator = Build();
        RefreshResult result = await orchestrator.RefreshAsync("UDID-X", "com.unknown");

        Assert.False(result.Success);
        Assert.Contains("not registered", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_MissingInputIpa_Fails()
    {
        await _registry.UpsertAsync(new AppRegistration(
            "com.example.app", "me@example.com", "TEAM", "UDID-1",
            Path.Combine(_dir, "does-not-exist.ipa")));
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("input IPA not found", result.Error);
        Assert.Empty(_devices.Installs);
    }

    [Fact]
    public async Task RefreshAsync_TwoFactorRequired_ReportsInteractiveLogin()
    {
        await RegisterAsync();
        _portal.OnAuthenticate = (_, _) =>
            new AppleLoginResult.TwoFactorRequired(
                new AppleLoginChallenge("adsid", "idms", TwoFactorKind.TrustedDevice));
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("interactive sign-in", result.Error);
        Assert.Equal(0, _signer.SignCalls);
    }

    [Fact]
    public async Task RefreshAsync_NoCredential_ReportsInteractiveLogin()
    {
        await RegisterAsync(udid: "UDID-NOCRED");
        await _registry.UpsertAsync(new AppRegistration(
            "com.example.app", "other@example.com", "TEAM", "UDID-NOCRED", _inputIpa));
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-NOCRED", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("interactive sign-in", result.Error);
    }

    [Fact]
    public async Task RefreshAsync_IdentityPrepFails_Fails()
    {
        await RegisterAsync();
        _identity.Throw = new InvalidOperationException("portal exploded");
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("could not prepare the signing identity", result.Error);
        Assert.Equal(0, _signer.SignCalls);
    }

    [Fact]
    public async Task RefreshAsync_SignFails_DoesNotInstall()
    {
        await RegisterAsync();
        _signer.Succeed = false;
        _signer.Error = "Code=85";
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("Code=85", result.Error);
        Assert.Empty(_devices.Installs);
    }

    [Fact]
    public async Task RefreshAsync_InstallFails_RecordsExpiryButFails()
    {
        await RegisterAsync();
        _devices.ThrowOnInstall = new InvalidOperationException("device asleep");
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(result.Success);
        Assert.Contains("iPhone install failed", result.Error);
        Assert.NotNull(result.NewExpiry); // signing succeeded; install didn't
    }

    [Fact]
    public async Task RefreshAsync_HungInstallBecomesUnknownAndKeepsSignerLeaseUntilTransferStops()
    {
        await RegisterAsync();
        _options.InstallTimeout = TimeSpan.FromMilliseconds(20);
        _options.InstallCancellationGrace = TimeSpan.FromMilliseconds(10);
        _devices.InstallCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshOrchestrator orchestrator = Build();

        RefreshResult first = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(first.Success);
        Assert.Equal("install-outcome-unknown", first.ErrorCode);
        Assert.True(orchestrator.IsDeviceMutationActive("UDID-1"));

        Task<RefreshResult> queuedBehindUnknown = orchestrator.RefreshAsync("UDID-1", "com.example.app");
        Task earlyWinner = await Task.WhenAny(queuedBehindUnknown, Task.Delay(50));
        Assert.NotSame(queuedBehindUnknown, earlyWinner);

        _devices.InstallCompletion.SetResult();
        RefreshResult second = await queuedBehindUnknown.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(second.Success, second.Error);
        Assert.False(orchestrator.IsDeviceMutationActive("UDID-1"));
    }

    [Fact]
    public async Task RefreshAsync_StalledInstallThatHardAbortsBecomesUnknownAndReleasesLease()
    {
        await RegisterAsync();
        _options.InstallTimeout = TimeSpan.FromMilliseconds(20);
        _options.InstallCancellationGrace = TimeSpan.FromMilliseconds(100);
        _devices.InstallCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _devices.CompleteInstallWhenCanceled = true;
        RefreshOrchestrator orchestrator = Build();

        RefreshResult first = await orchestrator.RefreshAsync("UDID-1", "com.example.app");

        Assert.False(first.Success);
        Assert.Equal("install-outcome-unknown", first.ErrorCode);
        Assert.False(orchestrator.IsDeviceMutationActive("UDID-1"));

        _devices.InstallCompletion = null;
        RefreshResult usbRecovery = await orchestrator.RefreshAsync("UDID-1", "com.example.app")
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(usbRecovery.Success, usbRecovery.Error);
        Assert.Equal(2, _devices.Installs.Count);
        Assert.False(orchestrator.IsDeviceMutationActive("UDID-1"));
    }

    [Fact]
    public async Task RefreshAsync_ReusesSession_AcrossMultipleRefreshes()
    {
        await RegisterAsync("com.example.a");
        await RegisterAsync("com.example.b");
        RefreshOrchestrator orchestrator = Build();

        await orchestrator.RefreshAsync("UDID-1", "com.example.a");
        await orchestrator.RefreshAsync("UDID-1", "com.example.b");

        // The session is cached after the first login.
        Assert.Equal(1, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task RefreshAsync_ExistingAuthorityOnlyWithoutCachedSession_NeverAuthenticatesOrMutates()
    {
        await RegisterAsync();
        RefreshOrchestrator orchestrator = Build();

        RefreshResult result = await orchestrator.RefreshAsync(
            "UDID-1",
            "com.example.app",
            RefreshExecutionPolicy.ExistingAuthorityOnly);

        Assert.False(result.Success);
        Assert.Equal("owner-action-required", result.ErrorCode);
        Assert.Equal(0, _portal.AuthenticateCalls);
        Assert.Equal(0, _identity.PrepareCalls);
        Assert.Equal(0, _signer.SignCalls);
        Assert.Empty(_devices.Installs);
    }

    [Fact]
    public async Task RefreshAsync_ExistingAuthorityOnlyWithCachedSession_DisablesCertificateCreation()
    {
        await RegisterAsync();
        var sessions = new SessionManager(
            _portal,
            _credentials,
            NullLogger<SessionManager>.Instance);
        sessions.RememberSession(new AppleSession(
            "me@example.com",
            "cached-adsid",
            "Owner",
            [0x01])
        {
            IdmsToken = "cached-idms-token",
        });
        var orchestrator = new RefreshOrchestrator(
            _registry,
            sessions,
            _identity,
            _signer,
            _devices,
            _options,
            NullLogger<RefreshOrchestrator>.Instance);

        RefreshResult result = await orchestrator.RefreshAsync(
            "UDID-1",
            "com.example.app",
            RefreshExecutionPolicy.ExistingAuthorityOnly);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, _portal.AuthenticateCalls);
        Assert.Equal([false], _identity.CertificateCreationPolicies);
        Assert.Equal(1, _signer.SignCalls);
        Assert.Single(_devices.Installs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
