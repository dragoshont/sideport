using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>
/// Default <see cref="ISessionManager"/>: caches one <see cref="AppleSession"/>
/// per Apple ID and re-authenticates unattended (using the stored credential)
/// on a cache miss. After the first interactive 2FA the anisette trusted-device
/// state persists, so unattended re-auth then succeeds without a second factor;
/// if a factor is unexpectedly required, callers get a clear
/// <see cref="InteractiveLoginRequiredException"/>.
///
/// Re-authentication for a given Apple ID is serialized so concurrent refreshes
/// don't trigger parallel logins (which would waste anisette one-time passwords
/// and risk Apple throttling).
/// </summary>
public sealed class SessionManager : ISessionManager
{
    private readonly IAppleDeveloperPortal _portal;
    private readonly IAppleCredentialProvider _credentials;
    private readonly ILogger<SessionManager> _logger;

    private readonly ConcurrentDictionary<string, AppleSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    public SessionManager(
        IAppleDeveloperPortal portal,
        IAppleCredentialProvider credentials,
        ILogger<SessionManager>? logger = null)
    {
        _portal = portal;
        _credentials = credentials;
        _logger = logger ?? NullLogger<SessionManager>.Instance;
    }

    public AppleSession? TryGetCachedSession(string appleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        return _sessions.GetValueOrDefault(appleId);
    }

    public async Task<AppleSession> GetSessionAsync(string appleId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(appleId, out AppleSession? cached))
            return cached;

        SemaphoreSlim gate = _locks.GetOrAdd(appleId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check under the lock: another caller may have just authenticated.
            if (_sessions.TryGetValue(appleId, out cached))
                return cached;

            string password = await _credentials.GetPasswordAsync(appleId, ct)
                ?? throw new InteractiveLoginRequiredException(appleId);

            AppleLoginResult result = await _portal.AuthenticateAsync(appleId, password, ct);
            return result switch
            {
                AppleLoginResult.Success success => Cache(appleId, success.Session),
                AppleLoginResult.TwoFactorRequired twoFactor =>
                    throw new InteractiveLoginRequiredException(appleId, twoFactor.Challenge),
                _ => throw new InvalidOperationException($"unexpected login result {result.GetType().Name}"),
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AppleLoginResult> SignInAsync(string appleId, CancellationToken ct = default)
    {
        string password = await _credentials.GetPasswordAsync(appleId, ct)
            ?? throw new InvalidOperationException($"no credential configured for '{appleId}'");

        AppleLoginResult result = await _portal.AuthenticateAsync(appleId, password, ct);
        if (result is AppleLoginResult.Success success)
            Cache(appleId, success.Session);
        return result;
    }

    public async Task<AppleSession> CompleteTwoFactorAsync(
        string appleId, AppleLoginChallenge challenge, string code, CancellationToken ct = default)
    {
        await _portal.SubmitTwoFactorCodeAsync(challenge, code, ct);

        string password = await _credentials.GetPasswordAsync(appleId, ct)
            ?? throw new InvalidOperationException($"no credential configured for '{appleId}'");

        AppleLoginResult result = await _portal.AuthenticateAsync(appleId, password, ct);
        return result switch
        {
            AppleLoginResult.Success success => Cache(appleId, success.Session),
            AppleLoginResult.TwoFactorRequired twoFactor =>
                throw new InteractiveLoginRequiredException(appleId, twoFactor.Challenge),
            _ => throw new InvalidOperationException($"unexpected login result {result.GetType().Name}"),
        };
    }

    public void RememberSession(AppleSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Cache(session.AppleId, session);
    }

    public void Invalidate(string appleId) => _sessions.TryRemove(appleId, out _);

    private AppleSession Cache(string appleId, AppleSession session)
    {
        _sessions[appleId] = session;
        _logger.LogInformation("cached GrandSlam session for {AppleId}", Redact(appleId));
        return session;
    }

    private static string Redact(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 3 ? "***" : value[..3] + "***";
}
