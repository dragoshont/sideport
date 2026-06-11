using System.Collections.Concurrent;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

public sealed record PersonalAppleAccessOptions(string? DefaultAppleId, string CredentialSource = "environment");

public sealed record PersonalAppleStatusDto(
    string Connector,
    string State,
    string SecretCustody,
    string? AppleIdHint,
    string Message,
    string? PendingChallengeId,
    string? PendingChallengeKind,
    IReadOnlyList<PersonalAppleTeamDto> Teams);

public sealed record PersonalAppleTeamDto(string TeamId, string Name, string Type);

public sealed record PersonalAppleSignInRequest(string AppleId);

public sealed record PersonalAppleCompleteTwoFactorRequest(string ChallengeId, string Code);

public interface IPersonalAppleAccess
{
    Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default);

    Task<PersonalAppleStatusDto> SignInAsync(PersonalAppleSignInRequest request, CancellationToken ct = default);

    Task<PersonalAppleStatusDto> CompleteTwoFactorAsync(PersonalAppleCompleteTwoFactorRequest request, CancellationToken ct = default);
}

public sealed class PersonalAppleAccess : IPersonalAppleAccess
{
    private readonly ISessionManager _sessions;
    private readonly IAppleDeveloperPortal _portal;
    private readonly PersonalAppleAccessOptions _options;
    private readonly ConcurrentDictionary<string, PendingChallenge> _challenges = new();
    private AppleSession? _lastSession;

    public PersonalAppleAccess(
        ISessionManager sessions,
        IAppleDeveloperPortal portal,
        PersonalAppleAccessOptions options)
    {
        _sessions = sessions;
        _portal = portal;
        _options = options;
    }

    public async Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default)
    {
        if (_lastSession is not null)
            return await StatusForSessionAsync(_lastSession, "Authenticated with Personal Apple ID connector.", ct).ConfigureAwait(false);

        PendingChallenge? pending = _challenges.Values.OrderByDescending(challenge => challenge.CreatedAt).FirstOrDefault();
        if (pending is not null)
            return PendingStatus(pending);

        string? appleId = NormalizeAppleId(_options.DefaultAppleId);
        return new PersonalAppleStatusDto(
            "personal-apple-id",
            appleId is null ? "not-configured" : "credential-configured",
            SecretCustody(),
            RedactAppleId(appleId),
            appleId is null
                ? MissingCredentialMessage()
                : $"Personal Apple ID is configured via {SecretCustodyLabel()}. Start sign-in when you are ready; 2FA may be required.",
            PendingChallengeId: null,
            PendingChallengeKind: null,
            Teams: []);
    }

    public async Task<PersonalAppleStatusDto> SignInAsync(PersonalAppleSignInRequest request, CancellationToken ct = default)
    {
        string appleId = NormalizeAppleId(request.AppleId)
            ?? throw new ArgumentException("Apple ID is required.", nameof(request));

        AppleLoginResult result = await _sessions.SignInAsync(appleId, ct).ConfigureAwait(false);
        if (result is AppleLoginResult.Success success)
        {
            _lastSession = success.Session;
            ClearChallengesFor(appleId);
            return await StatusForSessionAsync(success.Session, "Personal Apple ID sign-in succeeded.", ct).ConfigureAwait(false);
        }

        if (result is AppleLoginResult.TwoFactorRequired twoFactor)
        {
            string challengeId = Guid.NewGuid().ToString("N");
            var pending = new PendingChallenge(challengeId, appleId, twoFactor.Challenge, DateTimeOffset.UtcNow);
            _challenges[challengeId] = pending;
            return PendingStatus(pending);
        }

        throw new InvalidOperationException($"Unexpected login result {result.GetType().Name}.");
    }

    public async Task<PersonalAppleStatusDto> CompleteTwoFactorAsync(PersonalAppleCompleteTwoFactorRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ChallengeId))
            throw new ArgumentException("Challenge ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("2FA code is required.", nameof(request));

        if (!_challenges.TryRemove(request.ChallengeId, out PendingChallenge? pending))
            throw new KeyNotFoundException("Pending 2FA challenge was not found or has expired.");

        AppleSession session = await _sessions.CompleteTwoFactorAsync(pending.AppleId, pending.Challenge, request.Code.Trim(), ct).ConfigureAwait(false);
        _lastSession = session;
        ClearChallengesFor(pending.AppleId);
        return await StatusForSessionAsync(session, "Personal Apple ID 2FA completed and session cached.", ct).ConfigureAwait(false);
    }

    private async Task<PersonalAppleStatusDto> StatusForSessionAsync(AppleSession session, string message, CancellationToken ct)
    {
        IReadOnlyList<AppleTeam> teams = await _portal.ListTeamsAsync(session, ct).ConfigureAwait(false);
        return new PersonalAppleStatusDto(
            "personal-apple-id",
            "authenticated",
            "cached-grand-slam-session",
            RedactAppleId(session.AppleId),
            message,
            PendingChallengeId: null,
            PendingChallengeKind: null,
            Teams: teams.Select(team => new PersonalAppleTeamDto(team.TeamId, team.Name, team.Type)).ToArray());
    }

    private PersonalAppleStatusDto PendingStatus(PendingChallenge pending) => new(
        "personal-apple-id",
        "two-factor-required",
        SecretCustody(),
        RedactAppleId(pending.AppleId),
        "Apple requires a second factor. Enter the code locally in the portal; do not paste it into chat.",
        pending.Id,
        pending.Challenge.Kind.ToString(),
        Teams: []);

    private string SecretCustody()
    {
        if (string.Equals(_options.CredentialSource, "vault", StringComparison.OrdinalIgnoreCase))
            return "vaultwarden-via-bitwarden-cli";
        return "environment-or-sops";
    }

    private string SecretCustodyLabel()
    {
        if (string.Equals(_options.CredentialSource, "vault", StringComparison.OrdinalIgnoreCase))
            return "Vaultwarden through a Bitwarden CLI bridge";
        return "environment/SOPS secret custody";
    }

    private string MissingCredentialMessage()
    {
        if (string.Equals(_options.CredentialSource, "vault", StringComparison.OrdinalIgnoreCase))
            return "Configure SIDEPORT_PERSONAL_APPLE_ID and a matching Vaultwarden item reachable through bw serve before starting sign-in.";
        return "Configure SIDEPORT_PERSONAL_APPLE_ID and the matching SIDEPORT_APPLE_PW_* secret before starting sign-in.";
    }

    private void ClearChallengesFor(string appleId)
    {
        foreach ((string id, PendingChallenge challenge) in _challenges)
        {
            if (string.Equals(challenge.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
                _challenges.TryRemove(id, out _);
        }
    }

    private static string? NormalizeAppleId(string? appleId) =>
        string.IsNullOrWhiteSpace(appleId) ? null : appleId.Trim();

    private static string? RedactAppleId(string? appleId)
    {
        if (string.IsNullOrWhiteSpace(appleId)) return null;
        int at = appleId.IndexOf('@');
        if (at <= 1) return "***";
        return $"{appleId[0]}***{appleId[at..]}";
    }

    private sealed record PendingChallenge(string Id, string AppleId, AppleLoginChallenge Challenge, DateTimeOffset CreatedAt);
}