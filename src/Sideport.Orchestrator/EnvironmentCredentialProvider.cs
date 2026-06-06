namespace Sideport.Orchestrator;

/// <summary>
/// Default <see cref="IAppleCredentialProvider"/> that reads passwords from
/// environment variables of the form <c>SIDEPORT_APPLE_PW_&lt;SANITIZED-ID&gt;</c>,
/// where the Apple ID is uppercased and non-alphanumeric characters become
/// <c>_</c> (e.g. <c>me@example.com</c> → <c>SIDEPORT_APPLE_PW_ME_EXAMPLE_COM</c>).
///
/// On the host these variables are injected from SOPS/sealed-secrets at runtime,
/// so the secret never lands in the repo, config files, or process arguments.
/// </summary>
public sealed class EnvironmentCredentialProvider : IAppleCredentialProvider
{
    private const string Prefix = "SIDEPORT_APPLE_PW_";

    public Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        string variable = Prefix + Sanitize(appleId);
        string? value = Environment.GetEnvironmentVariable(variable);
        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    internal static string Sanitize(string appleId)
    {
        Span<char> buffer = stackalloc char[appleId.Length];
        for (int i = 0; i < appleId.Length; i++)
        {
            char c = appleId[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_';
        }
        return new string(buffer);
    }
}
