using System.Collections.Concurrent;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

public sealed class AppleAccountReplacementConnectRequest
{
    public string? AppleId { get; init; }
    public string? Password { get; init; }
    public override string ToString() => "AppleAccountReplacementConnectRequest { [REDACTED] }";
}

public sealed record AppleAccountReplacementTwoFactorRequest(string CandidateId, string Code)
{
    public override string ToString() => "AppleAccountReplacementTwoFactorRequest { [REDACTED] }";
}
public sealed record AppleAccountReplacementCandidateDto(
    string CandidateId,
    string State,
    string AppleIdHint,
    string AccountProfileId,
    IReadOnlyList<PersonalAppleTeamDto> Teams,
    DateTimeOffset ExpiresAt,
    string? ChallengeKind = null);

public sealed record AppleAccountReplacementContext(
    string CandidateId,
    string AppleId,
    string Password,
    AppleSession Session,
    IReadOnlyList<AppleTeam> Teams,
    DateTimeOffset ExpiresAt);

internal sealed class AppleAccountReplacementCandidateService(
    IAppleDeveloperPortal portal,
    IAppleCredentialManagement credentialManagement,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Candidate> _candidates = new(StringComparer.Ordinal);

    public async Task<AppleAccountReplacementCandidateDto> ConnectAsync(AppleAccountReplacementConnectRequest request, string actor, CancellationToken ct)
    {
        if (!credentialManagement.SupportsEntry) throw new AppleCredentialSourceReadOnlyException();
        string appleId = PersonalAppleAccess.ValidateAppleId(request.AppleId);
        string password = PersonalAppleAccess.ValidatePassword(request.Password);
        ManagedAppleCredentialMetadata? current = await credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        if (current is null || string.Equals(current.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
            throw new AppleAccountReplacementNotDifferentException();
        string candidateId = $"apple_candidate_{Guid.NewGuid():N}";
        DateTimeOffset expiresAt = _time.GetUtcNow().Add(Lifetime);
        AppleLoginResult result = await portal.AuthenticateAsync(appleId, password, ct).ConfigureAwait(false);
        Candidate candidate;
        if (result is AppleLoginResult.TwoFactorRequired twoFactor)
        {
            candidate = new Candidate(candidateId, appleId, password, AppleAccountIdentity.RequireActor(actor), expiresAt, twoFactor.Challenge, null, []);
            _candidates[candidateId] = candidate;
            return Project(candidate, "two-factor-required");
        }
        if (result is not AppleLoginResult.Success success) throw new AppleAuthenticationFailedException();
        IReadOnlyList<AppleTeam> teams = await portal.ListTeamsAsync(success.Session, ct).ConfigureAwait(false);
        candidate = new Candidate(candidateId, appleId, password, AppleAccountIdentity.RequireActor(actor), expiresAt, null, success.Session, teams);
        _candidates[candidateId] = candidate;
        return Project(candidate, "validated");
    }

    public async Task<AppleAccountReplacementCandidateDto> CompleteTwoFactorAsync(AppleAccountReplacementTwoFactorRequest request, string actor, CancellationToken ct)
    {
        Candidate candidate = Get(request.CandidateId, actor);
        string code = request.Code?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) throw new ArgumentException("2FA code must contain exactly six digits.");
        if (candidate.Challenge is null) return Project(candidate, "validated");
        try
        {
            await portal.SubmitTwoFactorCodeAsync(candidate.Challenge, code, ct).ConfigureAwait(false);
            AppleLoginResult result = await portal.AuthenticateAsync(candidate.AppleId, candidate.Password, ct).ConfigureAwait(false);
            if (result is not AppleLoginResult.Success success) throw new AppleTwoFactorInvalidException();
            IReadOnlyList<AppleTeam> teams = await portal.ListTeamsAsync(success.Session, ct).ConfigureAwait(false);
            candidate = candidate with { Challenge = null, Session = success.Session, Teams = teams };
            _candidates[candidate.Id] = candidate;
            return Project(candidate, "validated");
        }
        catch (OperationCanceledException) { throw; }
        catch (AppleUpstreamRateLimitedException) { _candidates.TryRemove(candidate.Id, out _); throw; }
        catch (AppleUpstreamUnavailableException) { _candidates.TryRemove(candidate.Id, out _); throw; }
        catch
        {
            _candidates.TryRemove(candidate.Id, out _);
            throw new AppleTwoFactorInvalidException();
        }
    }

    public AppleAccountReplacementContext Resolve(string candidateId, string actor)
    {
        Candidate candidate = Get(candidateId, actor);
        if (candidate.Session is null) throw new AppleChallengeExpiredException();
        return new(candidate.Id, candidate.AppleId, candidate.Password, candidate.Session, candidate.Teams, candidate.ExpiresAt);
    }

    public AppleAccountReplacementContext Consume(string candidateId, string actor)
    {
        Candidate candidate = Get(candidateId, actor);
        _candidates.TryRemove(candidateId, out _);
        if (candidate.Session is null) throw new AppleChallengeExpiredException();
        return new(candidate.Id, candidate.AppleId, candidate.Password, candidate.Session, candidate.Teams, candidate.ExpiresAt);
    }

    public Task<IReadOnlyList<AppleDevelopmentCertificate>> ListCertificatesAsync(AppleAccountReplacementContext candidate, string teamId, CancellationToken ct) =>
        portal.ListDevelopmentCertificatesAsync(candidate.Session, teamId, ct);

    public void Complete(string candidateId)
    {
        _candidates.TryRemove(candidateId, out _);
    }

    private Candidate Get(string id, string actor)
    {
        if (!_candidates.TryGetValue(id, out Candidate? candidate)) throw new AppleChallengeExpiredException();
        if (!string.Equals(candidate.Actor, actor, StringComparison.Ordinal)) throw new AppleChallengeExpiredException();
        if (candidate.ExpiresAt <= _time.GetUtcNow()) { _candidates.TryRemove(id, out _); throw new AppleChallengeExpiredException(); }
        return candidate;
    }

    private static AppleAccountReplacementCandidateDto Project(Candidate value, string state) => new(value.Id, state, AppleAccountIdentity.Redact(value.AppleId), AppleAccountIdentity.ProfileIdFor(value.AppleId), value.Teams.Select(t => new PersonalAppleTeamDto(t.TeamId, t.Name, t.Type)).ToArray(), value.ExpiresAt, value.Challenge?.Kind.ToString());
    private sealed record Candidate(string Id, string AppleId, string Password, string Actor, DateTimeOffset ExpiresAt, AppleLoginChallenge? Challenge, AppleSession? Session, IReadOnlyList<AppleTeam> Teams);
}

public sealed class AppleAccountReplacementNotDifferentException : Exception;
