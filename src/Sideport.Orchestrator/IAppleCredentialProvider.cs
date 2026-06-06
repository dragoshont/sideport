namespace Sideport.Orchestrator;

/// <summary>
/// Resolves the password for an Apple ID at refresh time. Kept as a narrow seam
/// so the secret is sourced host-side (env / SOPS-decrypted file) and never
/// stored in the app registry, the session cache, or any log (design invariant
/// #6). The orchestrator only ever holds the resolved value transiently.
/// </summary>
public interface IAppleCredentialProvider
{
    /// <summary>
    /// Return the password for <paramref name="appleId"/>, or <see langword="null"/>
    /// if no credential is configured for it.
    /// </summary>
    Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default);
}
