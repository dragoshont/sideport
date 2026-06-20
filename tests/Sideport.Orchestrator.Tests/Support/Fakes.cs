using Sideport.Core;

namespace Sideport.Orchestrator.Tests.Support;

/// <summary>A credential provider with an in-memory map of Apple ID → password.</summary>
internal sealed class FakeCredentialProvider : IAppleCredentialProvider
{
    private readonly Dictionary<string, string> _passwords = new();

    public FakeCredentialProvider Add(string appleId, string password)
    {
        _passwords[appleId] = password;
        return this;
    }

    public Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default) =>
        Task.FromResult(_passwords.GetValueOrDefault(appleId));
}

/// <summary>
/// A signing-identity provider that returns canned input paths and a configurable
/// expiry, optionally counting calls or throwing to model P4 failure.
/// </summary>
internal sealed class FakeSigningIdentityProvider : ISigningIdentityProvider
{
    public DateTimeOffset Expiry { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public Exception? Throw { get; set; }
    public int PrepareCalls { get; private set; }

    public string Pkcs12Path { get; set; } = "/tmp/identity.p12";
    public string ProfilePath { get; set; } = "/tmp/profile.mobileprovision";

    public Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session, string teamId, string bundleId, string deviceUdid, CancellationToken ct = default)
    {
        PrepareCalls++;
        if (Throw is not null)
            throw Throw;
        return Task.FromResult(new PreparedSigningInputs(Pkcs12Path, "pw", ProfilePath, Expiry));
    }
}

/// <summary>A signer whose result and timing are configurable.</summary>
internal sealed class FakeSigner : ISigner
{
    public bool Succeed { get; set; } = true;
    public string? Error { get; set; }
    public int SignCalls;
    public int Concurrency;
    public int MaxObservedConcurrency;
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
    {
        int now = Interlocked.Increment(ref Concurrency);
        InterlockedMax(ref MaxObservedConcurrency, now);
        Interlocked.Increment(ref SignCalls);
        try
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, ct);

            return Succeed
                ? new SignResult(true, request.OutputIpaPath, "com.example.app", null)
                : new SignResult(false, request.OutputIpaPath, null, Error ?? "sign failed");
        }
        finally
        {
            Interlocked.Decrement(ref Concurrency);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int observed;
        do { observed = target; if (value <= observed) return; }
        while (Interlocked.CompareExchange(ref target, value, observed) != observed);
    }
}

/// <summary>A device controller that records installs and can throw.</summary>
internal sealed class FakeDeviceController : IDeviceController
{
    public List<(string Udid, string IpaPath)> Installs { get; } = [];
    public Exception? ThrowOnInstall { get; set; }

    public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviceInfo>>([]);

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InstalledApp>>([]);

    public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
    {
        if (ThrowOnInstall is not null)
            throw ThrowOnInstall;
        Installs.Add((udid, ipaPath));
        return Task.CompletedTask;
    }

    public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
        Task.FromResult(new DeviceDiagnostics("ok", []));
}
