using System.Diagnostics;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.DeveloperApi;

namespace Sideport.Api.Onboarding;

public sealed record SystemStatusOptions(
    string StateDirectory,
    string WorkDirectory,
    bool MutationProtected);

public sealed record SystemStatusCheckDto(
    string Id,
    string Status,
    string Source,
    DateTimeOffset CheckedAt,
    string Scope,
    IReadOnlyList<string> AffectedResources,
    string Reason,
    string? NextAction);

public sealed record SystemStatusDto(
    bool Operational,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemStatusCheckDto> Checks);

/// <summary>
/// Authenticated operational truth for setup. The public readiness probe stays
/// shallow so a recoverable dependency problem cannot hide the repair UI.
/// </summary>
public sealed class SystemStatusService(
    IAnisetteProvider anisette,
    SignerOptions signer,
    IDeviceController devices,
    OperationStore operationStore,
    SystemStatusOptions options,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan AnisetteCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignerCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _anisetteGate = new(1, 1);
    private readonly SemaphoreSlim _signerGate = new(1, 1);
    private (DateTimeOffset CheckedAt, bool Passed)? _anisetteCache;
    private (DateTimeOffset CheckedAt, bool Passed)? _signerCache;

    public async Task<SystemStatusDto> GetAsync(CancellationToken ct = default)
    {
        DateTimeOffset checkedAt = _time.GetUtcNow();
        var checks = new List<SystemStatusCheckDto>(8)
        {
            Check(
                "mutation-protection",
                options.MutationProtected,
                checkedAt,
                "workspace",
                ["authenticated-mutations"],
                options.MutationProtected
                    ? "Authenticated changes are protected."
                    : "Open mode has no verified actor and is read-only.",
                options.MutationProtected ? null : "Configure bearer-token authentication or OIDC."),
        };

        // Write probes intentionally run before the read probe: a brand-new,
        // writable deployment may not have created its state directory yet.
        checks.Add(ProbeWritableDirectory(
            "state-writable",
            options.StateDirectory,
            checkedAt,
            "storage",
            ["sideport-state"],
            "Sideport can save durable setup state.",
            "Mount a writable persistent volume at the configured state directory."));
        checks.Add(ProbeReadableDirectory(options.StateDirectory, checkedAt));
        checks.Add(ProbeWritableDirectory(
            "work-writable",
            options.WorkDirectory,
            checkedAt,
            "storage",
            ["signed-app-work"],
            "Sideport can prepare a signed app.",
            "Make the configured signing work directory writable."));

        Task<SystemStatusCheckDto> anisetteCheck = ProbeAnisetteAsync(checkedAt, ct);
        Task<SystemStatusCheckDto> signerCheck = ProbeSignerAsync(checkedAt, ct);
        Task<SystemStatusCheckDto> deviceCheck = ProbeDeviceTransportAsync(checkedAt, ct);
        Task<SystemStatusCheckDto> operationCheck = ProbeOperationStoreAsync(checkedAt, ct);
        await Task.WhenAll(anisetteCheck, signerCheck, deviceCheck, operationCheck).ConfigureAwait(false);
        checks.Add(await anisetteCheck.ConfigureAwait(false));
        checks.Add(await signerCheck.ConfigureAwait(false));
        checks.Add(await deviceCheck.ConfigureAwait(false));
        checks.Add(await operationCheck.ConfigureAwait(false));

        return new SystemStatusDto(
            checks.All(check => string.Equals(check.Status, "pass", StringComparison.Ordinal)),
            checkedAt,
            checks);
    }

    private SystemStatusCheckDto ProbeReadableDirectory(string path, DateTimeOffset checkedAt)
    {
        bool passed;
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray();
            passed = true;
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            passed = false;
        }

        return Check(
            "state-readable",
            passed,
            checkedAt,
            "storage",
            ["sideport-state"],
            passed ? "Sideport can read durable setup state." : "Sideport cannot read its durable state directory.",
            passed ? null : "Restore access to the persistent Sideport state volume.");
    }

    private SystemStatusCheckDto ProbeWritableDirectory(
        string id,
        string path,
        DateTimeOffset checkedAt,
        string scope,
        IReadOnlyList<string> resources,
        string passedReason,
        string nextAction)
    {
        string? probePath = null;
        bool passed;
        try
        {
            Directory.CreateDirectory(path);
            probePath = Path.Combine(path, $".sideport-probe-{Guid.NewGuid():N}");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probePath);
            probePath = null;
            passed = true;
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            passed = false;
        }
        finally
        {
            if (probePath is not null)
            {
                try { File.Delete(probePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return Check(
            id,
            passed,
            checkedAt,
            scope,
            resources,
            passed ? passedReason : "Sideport cannot write the required directory.",
            passed ? null : nextAction);
    }

    private async Task<SystemStatusCheckDto> ProbeAnisetteAsync(DateTimeOffset checkedAt, CancellationToken ct)
    {
        await _anisetteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_anisetteCache is { } cached && checkedAt - cached.CheckedAt < AnisetteCacheLifetime)
                return AnisetteCheck(cached.Passed, checkedAt);

            bool passed;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                AnisetteHeaders headers = await anisette.GetHeadersAsync(timeout.Token).ConfigureAwait(false);
                passed = !string.IsNullOrWhiteSpace(headers.OneTimePassword);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                passed = false;
            }

            _anisetteCache = (checkedAt, passed);
            return AnisetteCheck(passed, checkedAt);
        }
        finally
        {
            _anisetteGate.Release();
        }
    }

    private static SystemStatusCheckDto AnisetteCheck(bool passed, DateTimeOffset checkedAt) =>
        Check(
            "anisette-headers",
            passed,
            checkedAt,
            "apple-signer",
            ["configured-apple-account"],
            passed ? "Provisioned Apple authentication headers are available." : "Provisioned Apple authentication headers are unavailable.",
            passed ? null : "Restore or provision the persistent anisette identity.");

    private async Task<SystemStatusCheckDto> ProbeSignerAsync(DateTimeOffset checkedAt, CancellationToken ct)
    {
        await _signerGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_signerCache is { } cached && checkedAt - cached.CheckedAt < SignerCacheLifetime)
                return SignerCheck(cached.Passed, checkedAt);

            bool passed = false;
            if (File.Exists(signer.SignerBinaryPath))
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = signer.SignerBinaryPath,
                        ArgumentList = { "-v" },
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                };
                bool started = false;
                try
                {
                    started = process.Start();
                    if (started)
                    {
                        Task stdout = process.StandardOutput.ReadToEndAsync(ct);
                        Task stderr = process.StandardError.ReadToEndAsync(ct);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(ProbeTimeout);
                        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                        passed = process.ExitCode == 0;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
                {
                    if (started)
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch (Exception killError) when (killError is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                        {
                            // A failed probe remains failed; never let cleanup hide it.
                        }
                    }
                    passed = false;
                }
            }

            _signerCache = (checkedAt, passed);
            return SignerCheck(passed, checkedAt);
        }
        finally
        {
            _signerGate.Release();
        }
    }

    private static SystemStatusCheckDto SignerCheck(bool passed, DateTimeOffset checkedAt) =>
        Check(
            "signer-executable",
            passed,
            checkedAt,
            "apple-signer",
            ["signer-binary"],
            passed ? "The signer executable responds to a bounded read-only probe." : "The configured signer could not be executed.",
            passed ? null : "Install the supported signer binary and verify its execute permission.");

    private async Task<SystemStatusCheckDto> ProbeDeviceTransportAsync(DateTimeOffset checkedAt, CancellationToken ct)
    {
        bool passed;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            DeviceDiagnostics diagnostics = await devices.DiagnoseAsync(timeout.Token).ConfigureAwait(false);
            passed = !string.Equals(diagnostics.Status, "blocked", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            passed = false;
        }

        return Check(
            "device-transport",
            passed,
            checkedAt,
            "iphone",
            ["usbmux-transport"],
            passed ? "The iPhone transport is available." : "Sideport cannot reach the iPhone transport.",
            passed ? null : "Connect the host usbmuxd socket and verify Sideport can access it.");
    }

    private async Task<SystemStatusCheckDto> ProbeOperationStoreAsync(DateTimeOffset checkedAt, CancellationToken ct)
    {
        bool passed;
        try
        {
            _ = await operationStore.ListAsync(limit: 1, ct: ct).ConfigureAwait(false);
            passed = true;
        }
        catch (OperationStoreException)
        {
            passed = false;
        }

        return Check(
            "operation-store",
            passed,
            checkedAt,
            "storage",
            ["operation-history"],
            passed ? "Durable operation history is available." : "Durable operation history is unavailable.",
            passed ? null : "Repair or restore the Sideport operation store before continuing.");
    }

    private static SystemStatusCheckDto Check(
        string id,
        bool passed,
        DateTimeOffset checkedAt,
        string scope,
        IReadOnlyList<string> resources,
        string reason,
        string? nextAction) =>
        new(
            id,
            passed ? "pass" : "fail",
            "live",
            checkedAt,
            scope,
            resources,
            reason,
            nextAction);
}
