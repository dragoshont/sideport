using System.Text.Json;
using Sideport.Api.Onboarding;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.DeveloperApi;

namespace Sideport.Api.Tests;

public sealed class SystemStatusServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sideport-system-status-{Guid.NewGuid():N}");

    public SystemStatusServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task GetAsync_SeparatesMutationStorageAndDependencyFailures_WithoutLeakingDetails()
    {
        const string secret = "TOP-SECRET-APPLE-PASSWORD";
        string invalidStatePath = $"{_directory}{Path.DirectorySeparatorChar}\0state-{secret}";
        string invalidWorkPath = $"{_directory}{Path.DirectorySeparatorChar}\0work-{secret}";
        string operationPath = Path.Combine(_directory, "operations.json");
        await File.WriteAllTextAsync(operationPath, $"{{\"token\":\"{secret}\"");

        var service = new SystemStatusService(
            new FailingAnisette(secret, invalidStatePath),
            new SignerOptions
            {
                SignerBinaryPath = Path.Combine(_directory, $"missing-signer-{secret}"),
            },
            new FailingDeviceController(secret, invalidWorkPath),
            new OperationStore(operationPath),
            new SystemStatusOptions(invalidStatePath, invalidWorkPath, MutationProtected: false));

        SystemStatusDto status = await service.GetAsync();

        Assert.False(status.Operational);
        Assert.Equal(8, status.Checks.Count);
        Dictionary<string, SystemStatusCheckDto> checks = status.Checks.ToDictionary(check => check.Id);
        Assert.Equal("fail", checks["mutation-protection"].Status);
        Assert.Equal("fail", checks["state-readable"].Status);
        Assert.Equal("fail", checks["state-writable"].Status);
        Assert.Equal("fail", checks["work-writable"].Status);
        Assert.Equal("fail", checks["anisette-headers"].Status);
        Assert.Equal("fail", checks["signer-executable"].Status);
        Assert.Equal("fail", checks["device-transport"].Status);
        Assert.Equal("fail", checks["operation-store"].Status);

        string response = JsonSerializer.Serialize(status);
        Assert.DoesNotContain(secret, response, StringComparison.Ordinal);
        Assert.DoesNotContain(_directory, response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_SignerProbeRequiresSuccessfulExitCode()
    {
        if (OperatingSystem.IsWindows())
            return;

        string signerPath = Path.Combine(_directory, "failing-signer");
        await File.WriteAllTextAsync(signerPath, "#!/bin/sh\nexit 7\n");
        File.SetUnixFileMode(
            signerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new SystemStatusService(
            new HealthyAnisette(),
            new SignerOptions { SignerBinaryPath = signerPath },
            new HealthyDeviceController(),
            new OperationStore(Path.Combine(_directory, "healthy-operations.json")),
            new SystemStatusOptions(_directory, _directory, MutationProtected: true));

        SystemStatusDto status = await service.GetAsync();

        Assert.Equal(
            "fail",
            status.Checks.Single(check => check.Id == "signer-executable").Status);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FailingAnisette(string secret, string privatePath) : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            Task.FromException<AnisetteClientInfo>(Failure());

        public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default) =>
            Task.FromException<AnisetteHeaders>(Failure());

        private Exception Failure() =>
            new InvalidOperationException($"anisette failed with {secret} at {privatePath}");
    }

    private sealed class HealthyAnisette : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteClientInfo("test", "test"));

        public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteHeaders("M", "O", "R", "L", DateTimeOffset.UtcNow));
    }

    private sealed class HealthyDeviceController : IDeviceController
    {
        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([]);

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string udid,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }

    private sealed class FailingDeviceController(string secret, string privatePath) : IDeviceController
    {
        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<DeviceInfo>>(Failure());

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string udid,
            CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<InstalledApp>>(Failure());

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) =>
            Task.FromException(Failure());

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromException<DeviceDiagnostics>(Failure());

        private Exception Failure() =>
            new IOException($"device transport exposed {secret} at {privatePath}");
    }
}
