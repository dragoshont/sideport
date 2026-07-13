using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>
/// Manages authenticated GrandSlam sessions across refreshes so Sideport does
/// not re-run the full SRP login (and re-fetch anisette) on every operation.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Return only an already-cached Apple session. This read never loads a
    /// stored credential and never authenticates with Apple.
    /// </summary>
    AppleSession? TryGetCachedSession(string appleId) => null;

    /// <summary>
    /// Get a usable session for <paramref name="appleId"/>: the cached one if
    /// present, otherwise a fresh unattended re-authentication using the stored
    /// credential. Throws <see cref="InteractiveLoginRequiredException"/> if a
    /// second factor is required (cannot be done unattended) or no credential is
    /// configured.
    /// </summary>
    Task<AppleSession> GetSessionAsync(string appleId, CancellationToken ct = default);

    /// <summary>
    /// Begin an interactive sign-in (operator-driven). Returns the login result,
    /// which may be a pending 2FA challenge for the caller to complete via
    /// <see cref="CompleteTwoFactorAsync"/>.
    /// </summary>
    Task<AppleLoginResult> SignInAsync(string appleId, CancellationToken ct = default);

    /// <summary>
    /// Submit a 2FA code for a pending challenge, then re-authenticate and cache
    /// the resulting session.
    /// </summary>
    Task<AppleSession> CompleteTwoFactorAsync(
        string appleId, AppleLoginChallenge challenge, string code, CancellationToken ct = default);

    /// <summary>
    /// Cache a session that was established by another authenticated flow, such
    /// as managed credential establishment. The caller must only provide a
    /// session returned by the Apple portal after successful authentication.
    /// </summary>
    void RememberSession(AppleSession session);

    /// <summary>Drop any cached session for an Apple ID (e.g. after a 401).</summary>
    void Invalidate(string appleId);
}
