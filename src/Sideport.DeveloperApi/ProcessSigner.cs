using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.Packaging;

namespace Sideport.DeveloperApi;

/// <summary>
/// <see cref="ISigner"/> that shells out to a signer binary sidecar (design §6):
/// <c>zsign</c> (MIT, proven on the homelab host) by default, migrating to
/// <c>rcodesign</c> (Apache-2.0) later. The binary path comes from
/// <see cref="SignerOptions"/>.
///
/// Re-signs are intended to run one-at-a-time (the single-signer rule, design
/// invariant #5); the serialization itself is enforced by the orchestrator (P6),
/// while this type performs one invocation.
/// </summary>
public sealed class ProcessSigner : ISigner
{
    private readonly SignerOptions _options;
    private readonly ILogger<ProcessSigner> _logger;

    public ProcessSigner(SignerOptions options, ILogger<ProcessSigner>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<ProcessSigner>.Instance;
    }

    public async Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.InputIpaPath))
            return Fail(request, $"input IPA not found: {request.InputIpaPath}");
        if (!File.Exists(request.SigningCertificatePkcs12Path))
            return Fail(request, $"signing certificate not found: {request.SigningCertificatePkcs12Path}");
        if (!File.Exists(request.ProvisioningProfilePath))
            return Fail(request, $"provisioning profile not found: {request.ProvisioningProfilePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputIpaPath)!);

        // Re-export the identity password-less into a private temp file so the
        // p12 password never appears on the signer's command line (visible via
        // /proc/<pid>/cmdline on a shared host — OWASP A02).
        PreparedSigningIdentity identity;
        try
        {
            identity = PreparedSigningIdentity.Create(
                request.SigningCertificatePkcs12Path, request.SigningCertificatePassword);
        }
        catch (Exception ex)
        {
            return Fail(request, $"could not load signing identity: {ex.Message}");
        }

        using (identity)
        {
            (int exitCode, string stdout, string stderr) result;
            try
            {
                result = await RunAsync(BuildArguments(request, identity.Pkcs12Path), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail(request, $"signer process failed to start: {ex.Message}");
            }

            if (result.exitCode != 0 || !File.Exists(request.OutputIpaPath))
            {
                string detail = string.IsNullOrWhiteSpace(result.stderr) ? result.stdout : result.stderr;
                _logger.LogError("signer exited {Code}: {Detail}", result.exitCode, Truncate(detail));
                return Fail(request, $"signer exited {result.exitCode}: {Truncate(detail)}");
            }

            // Verify the output is a well-formed signed IPA and read back its
            // bundle id, rather than trusting the signer's stdout formatting.
            string? bundleId;
            try
            {
                bundleId = IpaInspector.Inspect(request.OutputIpaPath).BundleIdentifier;
            }
            catch (Exception ex)
            {
                return Fail(request, $"signed IPA failed verification: {ex.Message}");
            }

            _logger.LogInformation("signed {BundleId} -> {Output}", bundleId, request.OutputIpaPath);
            return new SignResult(true, request.OutputIpaPath, bundleId, null);
        }
    }

    /// <summary>
    /// Build the signer argv for the configured <see cref="SignerKind"/>. The
    /// identity is the already-prepared password-less p12, so the password flag
    /// is always empty (the secret is never placed on the command line).
    /// </summary>
    internal IReadOnlyList<string> BuildArguments(SignRequest request, string preparedPkcs12Path) =>
        _options.Kind switch
        {
            SignerKind.Zsign =>
            [
                "-k", preparedPkcs12Path,
                "-p", string.Empty,
                "-m", request.ProvisioningProfilePath,
                "-o", request.OutputIpaPath,
                request.InputIpaPath,
            ],
            SignerKind.Rcodesign =>
            [
                "sign",
                "--p12-file", preparedPkcs12Path,
                "--p12-password", string.Empty,
                "--provisioning-profile", request.ProvisioningProfilePath,
                request.InputIpaPath,
                request.OutputIpaPath,
            ],
            _ => throw new InvalidOperationException($"unsupported signer kind {_options.Kind}"),
        };

    private async Task<(int exitCode, string stdout, string stderr)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_options.SignerBinaryPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"signer timed out after {_options.Timeout.TotalSeconds:0}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup; the process may have exited in the meantime.
        }
    }

    private static SignResult Fail(SignRequest request, string error) =>
        new(false, request.OutputIpaPath, null, error);

    private static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>Configuration for <see cref="ProcessSigner"/>.</summary>
public sealed class SignerOptions
{
    /// <summary>Absolute path to the signer binary (zsign or rcodesign).</summary>
    public string SignerBinaryPath { get; set; } = "/opt/sideport/zsign";

    /// <summary>Which signer flavor is at <see cref="SignerBinaryPath"/>.</summary>
    public SignerKind Kind { get; set; } = SignerKind.Zsign;

    /// <summary>Maximum time a single signer invocation may run.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Supported signer binaries.</summary>
public enum SignerKind { Zsign, Rcodesign }
