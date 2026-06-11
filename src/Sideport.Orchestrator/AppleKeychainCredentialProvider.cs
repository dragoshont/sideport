namespace Sideport.Orchestrator;

using System.Diagnostics;

/// <summary>
/// Options for <see cref="AppleKeychainCredentialProvider"/>.
/// </summary>
/// <param name="ServiceName">
/// The macOS keychain generic-password "service" (<c>-s</c>); the Apple ID is the
/// "account" (<c>-a</c>). Default <c>sideport-apple-pw</c>. Store a password with:
/// <code>security add-generic-password -s sideport-apple-pw -a me@example.com -w</code>
/// </param>
/// <param name="SecurityBinaryPath">Path to the macOS <c>security</c> CLI.</param>
public sealed record KeychainCredentialOptions(
    string ServiceName = "sideport-apple-pw",
    string SecurityBinaryPath = "/usr/bin/security");

/// <summary>
/// <see cref="IAppleCredentialProvider"/> for LOCAL macOS development. Resolves the
/// Apple password from the login keychain via <c>security find-generic-password</c>,
/// so a developer running Sideport on their own Mac can keep the password in
/// Keychain instead of an environment variable. macOS-only; opt in with
/// <c>Sideport:Apple:CredentialSource=keychain</c>. In-cluster (Linux) deployments
/// keep the default <see cref="EnvironmentCredentialProvider"/> (env injected from a
/// Kubernetes Secret, filled by SOPS or by Azure Key Vault via External Secrets).
///
/// Returns <see langword="null"/> on a genuine "not found" (matching
/// <see cref="EnvironmentCredentialProvider"/>) and never logs the password
/// (design invariant #6).
/// </summary>
public sealed class AppleKeychainCredentialProvider : IAppleCredentialProvider
{
    private readonly KeychainCredentialOptions _options;

    public AppleKeychainCredentialProvider(KeychainCredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ServiceName);
        ArgumentException.ThrowIfNullOrEmpty(options.SecurityBinaryPath);
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "AppleKeychainCredentialProvider is macOS-only. In-cluster / Linux runs must use the " +
                "default environment credential source (Sideport:Apple:CredentialSource=environment).");
        }

        _options = options;
    }

    public async Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);

        var psi = new ProcessStartInfo
        {
            FileName = _options.SecurityBinaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // ArgumentList (not a command string) => no shell, no injection. `-w`
        // prints ONLY the password to stdout.
        psi.ArgumentList.Add("find-generic-password");
        psi.ArgumentList.Add("-s");
        psi.ArgumentList.Add(_options.ServiceName);
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add(appleId);
        psi.ArgumentList.Add("-w");

        using var process = new Process { StartInfo = psi };
        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        // Drain stderr so the pipe cannot deadlock. It is NOT surfaced in any
        // error (it can echo the item name); a miss writes here + exits non-zero.
        _ = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        // Non-zero = item not found OR keychain locked. For local dev we treat
        // both as "no credential configured" (surfaces as not-configured, never
        // a silent wrong-credential). The password is never logged.
        if (process.ExitCode != 0)
            return null;

        string password = stdout.TrimEnd('\r', '\n');
        return string.IsNullOrEmpty(password) ? null : password;
    }
}
