using System.Collections.Concurrent;
using System.Net;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

public sealed record PersonalAppleAccessOptions(string? DefaultAppleId, string CredentialSource = "environment");

public sealed record PersonalAppleBlockedReasonDto(string Code, string Message);

public sealed record PersonalAppleCredentialEntryDto(
    bool Supported,
    bool AllowedNow,
    PersonalAppleBlockedReasonDto? BlockedReason);

public sealed record PersonalAppleStatusDto(
    string Connector,
    string State,
    string SecretCustody,
    string? AppleIdHint,
    string Message,
    string? PendingChallengeId,
    string? PendingChallengeKind,
    IReadOnlyList<PersonalAppleTeamDto> Teams,
    string CredentialSource = AppleCredentialSources.Environment,
    string? AccountProfileId = null,
    PersonalAppleCredentialEntryDto? CredentialEntry = null,
    string? SelectedTeamId = null,
    DateTimeOffset? TeamValidatedAt = null,
    DateTimeOffset? LastAuthenticatedAt = null,
    DateTimeOffset? AuthValidatedAt = null,
    DateTimeOffset? PendingChallengeExpiresAt = null);

public sealed record PersonalAppleTeamDto(string TeamId, string Name, string Type);

public sealed record PersonalAppleSignInRequest(string? AppleId = null);

public sealed class PersonalAppleConnectRequest
{
    public string? AppleId { get; init; }
    public string? Password { get; init; }

    public override string ToString() => "PersonalAppleConnectRequest { [REDACTED] }";
}

public sealed class PersonalAppleCompleteTwoFactorRequest
{
    public PersonalAppleCompleteTwoFactorRequest() { }

    public PersonalAppleCompleteTwoFactorRequest(string challengeId, string code)
    {
        ChallengeId = challengeId;
        Code = code;
    }

    public string? ChallengeId { get; init; }
    public string? Code { get; init; }

    public override string ToString() => "PersonalAppleCompleteTwoFactorRequest { [REDACTED] }";
}

public sealed record PersonalAppleTeamSelectionRequest(string AccountProfileId, string TeamId);

public sealed record PersonalAppleSigningPreflightRequest(
    string AccountProfileId,
    string TeamId,
    string? CandidateId = null,
    string? CurrentAccountProfileId = null);

public sealed record PersonalAppleSigningCertificateDto(string Id, string? SerialSuffix, DateTimeOffset? ExpiresAt);

public sealed record PersonalAppleSigningPreflightDto(
    string PreflightId,
    DateTimeOffset ExpiresAt,
    string AccountProfileId,
    string TeamId,
    SigningIdentityInspection LocalIdentity,
    IReadOnlyList<PersonalAppleSigningCertificateDto> AppleCertificates,
    string Impact,
    bool RequiresAcknowledgement,
    string InventoryVersion,
    int RegistrationCount,
    int DeviceCount,
    int ProfileCount);

public sealed record PersonalAppleCutoverRequest(
    string PreflightId,
    string InventoryVersion,
    IReadOnlyList<string> AcknowledgedCertificateIds,
    IReadOnlyList<string> AcknowledgedImpactCodes,
    string IdempotencyKey);

public sealed record PersonalAppleConnectResult(PersonalAppleStatusDto Status, string Outcome);

public sealed record PersonalAppleTwoFactorResult(PersonalAppleStatusDto Status, string Outcome);

/// <summary>
/// Server-only signing context for an already validated account. Endpoints must
/// never serialize this value: the raw Apple ID is resolved only when the
/// operation service creates a durable registration.
/// </summary>
public sealed record PersonalAppleInstallContext(
    string AppleId,
    string AccountProfileId,
    string TeamId,
    DateTimeOffset AuthValidatedAt);

/// <summary>
/// Read-only Apple signing facts for an install preflight. The raw Apple ID is
/// server-only and this record must never be returned from an endpoint.
/// </summary>
public sealed record PersonalAppleInstallPreflightContext(
    PersonalAppleInstallContext Install,
    IReadOnlyList<AppleDevelopmentCertificate> Certificates);

public interface IPersonalAppleAccess
{
    Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default);

    Task<PersonalAppleConnectResult> ConnectAsync(
        PersonalAppleConnectRequest request,
        string actor,
        CancellationToken ct = default);

    Task<PersonalAppleStatusDto> SignInAsync(
        PersonalAppleSignInRequest request,
        string? actor = null,
        CancellationToken ct = default);

    Task<PersonalAppleTwoFactorResult> CompleteTwoFactorAsync(
        PersonalAppleCompleteTwoFactorRequest request,
        string? actor = null,
        CancellationToken ct = default);

    string? PendingChallengeAccountProfileId(string challengeId, string actor);

    Task<PersonalAppleInstallContext> ResolveFreshInstallContextAsync(
        string accountProfileId,
        CancellationToken ct = default);

    Task<PersonalAppleInstallPreflightContext> ResolveFreshInstallPreflightContextAsync(
        string accountProfileId,
        CancellationToken ct = default);

    Task<PersonalAppleStatusDto> SelectTeamAsync(
        PersonalAppleTeamSelectionRequest request,
        string actor,
        CancellationToken ct = default);

    Task<PersonalAppleSigningPreflightContext> ResolveSigningPreflightAsync(
        PersonalAppleSigningPreflightRequest request,
        CancellationToken ct = default) =>
        Task.FromException<PersonalAppleSigningPreflightContext>(new NotSupportedException("Signing cutover is unavailable."));
}

public sealed record PersonalAppleSigningPreflightContext(
    PersonalAppleInstallContext Install,
    string CurrentAppleId,
    string CurrentAccountProfileId,
    string CurrentTeamId,
    IReadOnlyList<AppleDevelopmentCertificate> Certificates,
    SigningIdentityInspection LocalIdentity,
    AppleSession Session);

internal sealed class PersonalAppleAccess : IPersonalAppleAccess
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AuthenticationFreshness = TimeSpan.FromMinutes(15);

    private readonly ISessionManager _sessions;
    private readonly IAppleDeveloperPortal _portal;
    private readonly PersonalAppleAccessOptions _options;
    private readonly IAppleCredentialManagement _credentialManagement;
    private readonly AppleAccountStateStore _accountState;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, PendingChallenge> _challenges = new();
    private AuthenticatedSession? _lastAuthentication;

    public PersonalAppleAccess(
        ISessionManager sessions,
        IAppleDeveloperPortal portal,
        PersonalAppleAccessOptions options,
        IAppleCredentialManagement credentialManagement,
        AppleAccountStateStore accountState,
        TimeProvider? timeProvider = null)
    {
        _sessions = sessions;
        _portal = portal;
        _options = options;
        _credentialManagement = credentialManagement;
        _accountState = accountState;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default)
    {
        RemoveExpiredChallenges();
        ManagedAppleCredentialMetadata? managed = await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        string? appleId = _credentialManagement.SupportsEntry
            ? managed?.AppleId
            : NormalizeAppleId(_options.DefaultAppleId);
        string? accountProfileId = appleId is null ? null : AppleAccountIdentity.ProfileIdFor(appleId);
        AppleAccountState? state = await _accountState.ReadAsync(ct).ConfigureAwait(false);
        if (!string.Equals(state?.AccountProfileId, accountProfileId, StringComparison.Ordinal))
            state = null;

        PendingChallenge? pending = _challenges.Values
            .Where(challenge => appleId is null || string.Equals(challenge.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(challenge => challenge.CreatedAt)
            .FirstOrDefault();
        if (pending is not null)
            return StatusFrom(pending, state);

        AuthenticatedSession? authenticated = FreshAuthenticationFor(accountProfileId, state);
        string status = appleId is null
            ? "not-configured"
            : authenticated is not null
                ? "validated-recently"
                : state is not null
                    ? "validation-stale"
                    : "credential-configured";
        string message = status switch
        {
            "validated-recently" => "The Personal Apple ID was validated recently.",
            "validation-stale" => "Sign in to Apple again before changing the team or signer.",
            "not-configured" => MissingCredentialMessage(),
            _ => $"Personal Apple ID is configured via {SecretCustodyLabel()}. Start sign-in when you are ready; 2FA may be required.",
        };
        return BuildStatus(
            status,
            appleId is null ? null : AppleAccountIdentity.Redact(appleId),
            message,
            accountProfileId,
            state,
            pending: null,
            state?.AuthValidatedAt);
    }

    public async Task<PersonalAppleConnectResult> ConnectAsync(
        PersonalAppleConnectRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        if (!_credentialManagement.SupportsEntry)
            throw new AppleCredentialSourceReadOnlyException();

        string appleId = ValidateAppleId(request.AppleId);
        string password = ValidatePassword(request.Password);
        ManagedAppleCredentialMetadata? existing = await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
            throw new AppleAccountReplacementRequiresCutoverException();

        AppleLoginResult result = await AuthenticateCandidateAsync(appleId, password, ct).ConfigureAwait(false);
        if (result is AppleLoginResult.TwoFactorRequired twoFactor)
        {
            PendingChallenge pending = AddChallenge(
                appleId,
                twoFactor.Challenge,
                verifiedActor,
                candidatePassword: password,
                managedConnect: true);
            AppleAccountState? state = await MatchingStateAsync(appleId, ct).ConfigureAwait(false);
            return new PersonalAppleConnectResult(StatusFrom(pending, state), "two-factor-required");
        }

        if (result is not AppleLoginResult.Success success)
            throw new AppleAuthenticationFailedException();

        IReadOnlyList<AppleTeam> teams = await ListTeamsSafelyAsync(success.Session, ct).ConfigureAwait(false);
        ManagedAppleCredentialCommit commit = await _credentialManagement.CommitAuthenticatedAsync(
            appleId,
            password,
            verifiedActor,
            ct).ConfigureAwait(false);
        _sessions.RememberSession(success.Session);
        AppleAccountState stateAfterAuth = await RecordAuthenticationAsync(success.Session, teams, verifiedActor, ct).ConfigureAwait(false);
        ClearChallengesFor(appleId);
        PersonalAppleStatusDto status = BuildAuthenticatedStatus(success.Session, stateAfterAuth);
        return new PersonalAppleConnectResult(status, commit.Created ? "created" : "updated");
    }

    public async Task<PersonalAppleStatusDto> SignInAsync(
        PersonalAppleSignInRequest request,
        string? actor = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? requestedAppleId = NormalizeAppleId(request.AppleId);
        ManagedAppleCredentialMetadata? managed = requestedAppleId is null && _credentialManagement.SupportsEntry
            ? await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false)
            : null;
        string appleId = requestedAppleId
            ?? managed?.AppleId
            ?? (_credentialManagement.SupportsEntry ? null : NormalizeAppleId(_options.DefaultAppleId))
            ?? throw new ArgumentException("No configured Apple account is available for sign-in.", nameof(request));

        string verifiedActor = string.IsNullOrWhiteSpace(actor) ? "system:interactive-sign-in" : AppleAccountIdentity.RequireActor(actor);
        AppleLoginResult result;
        try
        {
            result = await _sessions.SignInAsync(appleId, ct).ConfigureAwait(false);
        }
        catch (AppleCredentialStoreException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("no credential configured", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClassifyAppleFailure(ex, twoFactorSubmission: false);
        }

        if (result is AppleLoginResult.Success success)
        {
            IReadOnlyList<AppleTeam> teams = await ListTeamsSafelyAsync(success.Session, ct).ConfigureAwait(false);
            AppleAccountState state = await RecordAuthenticationAsync(success.Session, teams, verifiedActor, ct).ConfigureAwait(false);
            ClearChallengesFor(appleId);
            return BuildAuthenticatedStatus(success.Session, state);
        }

        if (result is AppleLoginResult.TwoFactorRequired twoFactor)
        {
            PendingChallenge pending = AddChallenge(appleId, twoFactor.Challenge, verifiedActor, candidatePassword: null, managedConnect: false);
            AppleAccountState? state = await MatchingStateAsync(appleId, ct).ConfigureAwait(false);
            return StatusFrom(pending, state);
        }

        throw new AppleAuthenticationFailedException();
    }

    public async Task<PersonalAppleTwoFactorResult> CompleteTwoFactorAsync(
        PersonalAppleCompleteTwoFactorRequest request,
        string? actor = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ChallengeId))
            throw new ArgumentException("Challenge ID is required.", nameof(request));
        string verifiedActor = string.IsNullOrWhiteSpace(actor) ? "system:interactive-sign-in" : AppleAccountIdentity.RequireActor(actor);
        PendingChallenge pending = TakeChallenge(request.ChallengeId, verifiedActor);
        string code = request.Code?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("2FA code must contain exactly six digits.", nameof(request));

        if (pending.ManagedConnect)
        {
            if (pending.CandidatePassword is null)
                throw new AppleChallengeExpiredException();
            try
            {
                await _portal.SubmitTwoFactorCodeAsync(pending.Challenge, code, ct).ConfigureAwait(false);
                AppleLoginResult result = await _portal.AuthenticateAsync(pending.AppleId, pending.CandidatePassword, ct).ConfigureAwait(false);
                if (result is not AppleLoginResult.Success success)
                    throw new AppleTwoFactorInvalidException();
                IReadOnlyList<AppleTeam> teams = await ListTeamsSafelyAsync(success.Session, ct).ConfigureAwait(false);
                ManagedAppleCredentialCommit commit = await _credentialManagement.CommitAuthenticatedAsync(
                    pending.AppleId,
                    pending.CandidatePassword,
                    verifiedActor,
                    ct).ConfigureAwait(false);
                _sessions.RememberSession(success.Session);
                AppleAccountState state = await RecordAuthenticationAsync(success.Session, teams, verifiedActor, ct).ConfigureAwait(false);
                ClearChallengesFor(pending.AppleId);
                return new PersonalAppleTwoFactorResult(
                    BuildAuthenticatedStatus(success.Session, state),
                    commit.Created ? "connected-created" : "connected-updated");
            }
            catch (AppleTwoFactorInvalidException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException &&
                                       ex is not AppleCredentialStoreException &&
                                       ex is not AppleAccountStateStoreException &&
                                       ex is not AppleUpstreamUnavailableException &&
                                       ex is not AppleUpstreamRateLimitedException)
            {
                throw ClassifyAppleFailure(ex, twoFactorSubmission: true);
            }
        }

        try
        {
            AppleSession session = await _sessions.CompleteTwoFactorAsync(pending.AppleId, pending.Challenge, code, ct).ConfigureAwait(false);
            IReadOnlyList<AppleTeam> teams = await ListTeamsSafelyAsync(session, ct).ConfigureAwait(false);
            AppleAccountState state = await RecordAuthenticationAsync(session, teams, verifiedActor, ct).ConfigureAwait(false);
            ClearChallengesFor(pending.AppleId);
            return new PersonalAppleTwoFactorResult(BuildAuthenticatedStatus(session, state), "signed-in");
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   ex is not AppleAccountStateStoreException &&
                                   ex is not AppleCredentialStoreException &&
                                   ex is not AppleTwoFactorInvalidException &&
                                   ex is not AppleUpstreamUnavailableException &&
                                   ex is not AppleUpstreamRateLimitedException)
        {
            throw ClassifyAppleFailure(ex, twoFactorSubmission: true);
        }
    }

    public string? PendingChallengeAccountProfileId(string challengeId, string actor)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(actor))
            return null;
        if (!_challenges.TryGetValue(challengeId, out PendingChallenge? pending) ||
            !string.Equals(pending.Actor, actor, StringComparison.Ordinal) ||
            pending.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            return null;
        }
        return AppleAccountIdentity.ProfileIdFor(pending.AppleId);
    }

    public async Task<PersonalAppleInstallContext> ResolveFreshInstallContextAsync(
        string accountProfileId,
        CancellationToken ct = default)
    {
        (PersonalAppleInstallContext context, _) =
            await ResolveFreshInstallContextCoreAsync(accountProfileId, ct).ConfigureAwait(false);
        return context;
    }

    public async Task<PersonalAppleInstallPreflightContext> ResolveFreshInstallPreflightContextAsync(
        string accountProfileId,
        CancellationToken ct = default)
    {
        (PersonalAppleInstallContext context, AuthenticatedSession authenticated) =
            await ResolveFreshInstallContextCoreAsync(accountProfileId, ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<AppleDevelopmentCertificate> certificates =
                await _portal.ListDevelopmentCertificatesAsync(
                    authenticated.Session,
                    context.TeamId,
                    ct).ConfigureAwait(false);
            return new PersonalAppleInstallPreflightContext(context, certificates);
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   ex is not AppleCredentialStoreException &&
                                   ex is not AppleAccountStateStoreException)
        {
            throw new AppleCertificateInventoryUnavailableException(ex);
        }
    }

    private async Task<(PersonalAppleInstallContext Context, AuthenticatedSession Authentication)>
        ResolveFreshInstallContextCoreAsync(
            string accountProfileId,
            CancellationToken ct)
    {
        string requestedProfileId = accountProfileId?.Trim() ?? string.Empty;
        if (requestedProfileId.Length == 0)
            throw new ArgumentException("Account profile ID is required.", nameof(accountProfileId));

        ManagedAppleCredentialMetadata? managed = await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        string? appleId = _credentialManagement.SupportsEntry
            ? managed?.AppleId
            : NormalizeAppleId(_options.DefaultAppleId);
        if (appleId is null ||
            !string.Equals(AppleAccountIdentity.ProfileIdFor(appleId), requestedProfileId, StringComparison.Ordinal))
        {
            throw new AppleAccountProfileNotFoundException();
        }

        AppleAccountState? state = await _accountState.ReadAsync(ct).ConfigureAwait(false);
        AuthenticatedSession? authenticated = FreshAuthenticationFor(requestedProfileId, state);
        if (authenticated is null)
            throw new AppleTeamSelectionStaleException();
        if (string.IsNullOrWhiteSpace(state!.SelectedTeamId) ||
            !state.Teams.Any(team => string.Equals(team.TeamId, state.SelectedTeamId, StringComparison.Ordinal)))
        {
            throw new AppleTeamNotReturnedException();
        }

        return (
            new PersonalAppleInstallContext(
                appleId,
                requestedProfileId,
                state.SelectedTeamId,
                state.AuthValidatedAt),
            authenticated);
    }

    public async Task<PersonalAppleStatusDto> SelectTeamAsync(
        PersonalAppleTeamSelectionRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        string accountProfileId = request.AccountProfileId?.Trim() ?? string.Empty;
        string teamId = request.TeamId?.Trim() ?? string.Empty;
        if (accountProfileId.Length == 0)
            throw new ArgumentException("Account profile ID is required.", nameof(request));
        if (teamId.Length == 0)
            throw new ArgumentException("Team ID is required.", nameof(request));

        ManagedAppleCredentialMetadata? managed = await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        string? appleId = _credentialManagement.SupportsEntry
            ? managed?.AppleId
            : NormalizeAppleId(_options.DefaultAppleId);
        if (appleId is null ||
            !string.Equals(AppleAccountIdentity.ProfileIdFor(appleId), accountProfileId, StringComparison.Ordinal))
        {
            throw new AppleAccountProfileNotFoundException();
        }

        AppleAccountState? current = await _accountState.ReadAsync(ct).ConfigureAwait(false);
        if (FreshAuthenticationFor(accountProfileId, current) is null)
            throw new AppleTeamSelectionStaleException();

        AppleAccountState selected = await _accountState.SelectTeamAsync(
            accountProfileId,
            teamId,
            _timeProvider.GetUtcNow(),
            AuthenticationFreshness,
            verifiedActor,
            ct).ConfigureAwait(false);
        return BuildStatus(
            "validated-recently",
            selected.AppleIdHint,
            "Apple developer team selection was saved.",
            selected.AccountProfileId,
            selected,
            pending: null,
            selected.AuthValidatedAt);
    }

    public async Task<PersonalAppleSigningPreflightContext> ResolveSigningPreflightAsync(
        PersonalAppleSigningPreflightRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string accountProfileId = request.AccountProfileId?.Trim() ?? string.Empty;
        string teamId = request.TeamId?.Trim() ?? string.Empty;
        if (accountProfileId.Length == 0 || teamId.Length == 0)
            throw new ArgumentException("Account profile ID and team ID are required.", nameof(request));
        ManagedAppleCredentialMetadata? managed = await _credentialManagement.ReadMetadataAsync(ct).ConfigureAwait(false);
        string? appleId = _credentialManagement.SupportsEntry ? managed?.AppleId : NormalizeAppleId(_options.DefaultAppleId);
        AppleAccountState? state = await _accountState.ReadAsync(ct).ConfigureAwait(false);
        AuthenticatedSession? authenticated = FreshAuthenticationFor(accountProfileId, state);
        if (appleId is null || state is null || authenticated is null ||
            !string.Equals(state.AccountProfileId, accountProfileId, StringComparison.Ordinal))
            throw new AppleTeamSelectionStaleException();
        if (!state.Teams.Any(team => string.Equals(team.TeamId, teamId, StringComparison.Ordinal)))
            throw new AppleTeamNotReturnedException();
        var install = new PersonalAppleInstallContext(appleId, accountProfileId, teamId, state.AuthValidatedAt);
        IReadOnlyList<AppleDevelopmentCertificate> certificates =
            await _portal.ListDevelopmentCertificatesAsync(authenticated.Session, install.TeamId, ct).ConfigureAwait(false);
        return new PersonalAppleSigningPreflightContext(
            install,
            appleId,
            accountProfileId,
            state.SelectedTeamId ?? teamId,
            certificates,
            new SigningIdentityInspection("unknown", null, null),
            authenticated.Session);
    }

    private async Task<AppleLoginResult> AuthenticateCandidateAsync(string appleId, string password, CancellationToken ct)
    {
        try
        {
            return await _portal.AuthenticateAsync(appleId, password, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClassifyAppleFailure(ex, twoFactorSubmission: false);
        }
    }

    private async Task<IReadOnlyList<AppleTeam>> ListTeamsSafelyAsync(AppleSession session, CancellationToken ct)
    {
        try
        {
            return await _portal.ListTeamsAsync(session, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClassifyAppleFailure(ex, twoFactorSubmission: false);
        }
    }

    private async Task<AppleAccountState> RecordAuthenticationAsync(
        AppleSession session,
        IReadOnlyList<AppleTeam> teams,
        string actor,
        CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        AppleAccountState state = await _accountState.RecordAuthenticationAsync(
            session.AppleId,
            teams,
            now,
            actor,
            ct).ConfigureAwait(false);
        _lastAuthentication = new AuthenticatedSession(session, now);
        return state;
    }

    private PersonalAppleStatusDto BuildAuthenticatedStatus(AppleSession session, AppleAccountState state) =>
        BuildStatus(
            "validated-recently",
            AppleAccountIdentity.Redact(session.AppleId),
            "Personal Apple ID sign-in succeeded.",
            state.AccountProfileId,
            state,
            pending: null,
            state.AuthValidatedAt);

    private PersonalAppleStatusDto StatusFrom(PendingChallenge pending, AppleAccountState? state) =>
        BuildStatus(
            "two-factor-required",
            AppleAccountIdentity.Redact(pending.AppleId),
            "Apple requires a second factor. Enter the code locally in the portal; do not paste it into chat.",
            AppleAccountIdentity.ProfileIdFor(pending.AppleId),
            state,
            pending,
            state?.AuthValidatedAt);

    private PersonalAppleStatusDto BuildStatus(
        string status,
        string? appleIdHint,
        string message,
        string? accountProfileId,
        AppleAccountState? state,
        PendingChallenge? pending,
        DateTimeOffset? lastAuthenticatedAt)
    {
        PersonalAppleTeamDto[] teams = state?.Teams
            .Select(team => new PersonalAppleTeamDto(team.TeamId, team.Name, team.Type))
            .ToArray() ?? [];
        var credentialEntry = _credentialManagement.SupportsEntry
            ? new PersonalAppleCredentialEntryDto(
                Supported: true,
                AllowedNow: false,
                new PersonalAppleBlockedReasonDto(
                    "credential-entry-request-context-required",
                    "Credential entry availability depends on the authenticated request transport."))
            : new PersonalAppleCredentialEntryDto(
                Supported: false,
                AllowedNow: false,
                new PersonalAppleBlockedReasonDto(
                    "credential-source-read-only",
                    "This deployment reads its Apple credential from host-side custody."));
        return new PersonalAppleStatusDto(
            "personal-apple-id",
            status,
            SecretCustody(),
            appleIdHint,
            message,
            pending?.Id,
            pending?.Challenge.Kind.ToString(),
            teams,
            _credentialManagement.Source,
            accountProfileId,
            credentialEntry,
            state?.SelectedTeamId,
            state?.TeamValidatedAt,
            lastAuthenticatedAt,
            state?.AuthValidatedAt,
            pending?.ExpiresAt);
    }

    private async Task<AppleAccountState?> MatchingStateAsync(string appleId, CancellationToken ct)
    {
        AppleAccountState? state = await _accountState.ReadAsync(ct).ConfigureAwait(false);
        return string.Equals(state?.AccountProfileId, AppleAccountIdentity.ProfileIdFor(appleId), StringComparison.Ordinal)
            ? state
            : null;
    }

    private PendingChallenge AddChallenge(
        string appleId,
        AppleLoginChallenge challenge,
        string? actor,
        string? candidatePassword,
        bool managedConnect)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string challengeId = Guid.NewGuid().ToString("N");
        var pending = new PendingChallenge(
            challengeId,
            appleId,
            challenge,
            AppleAccountIdentity.RequireActor(actor),
            candidatePassword,
            managedConnect,
            now,
            now.Add(ChallengeLifetime));
        _challenges[challengeId] = pending;
        pending.ExpiryTimer = _timeProvider.CreateTimer(
            static state =>
            {
                var (owner, id) = ((PersonalAppleAccess Owner, string Id))state!;
                owner.RemoveChallenge(id);
            },
            (this, challengeId),
            ChallengeLifetime,
            Timeout.InfiniteTimeSpan);
        return pending;
    }

    private PendingChallenge TakeChallenge(string challengeId, string actor)
    {
        if (!_challenges.TryGetValue(challengeId, out PendingChallenge? pending))
            throw new AppleChallengeNotFoundException();
        if (!string.Equals(pending.Actor, actor, StringComparison.Ordinal))
            throw new AppleChallengeNotFoundException();
        if (pending.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            RemoveChallenge(challengeId);
            throw new AppleChallengeExpiredException();
        }
        if (!_challenges.TryRemove(challengeId, out PendingChallenge? removed))
            throw new AppleChallengeNotFoundException();
        removed.ExpiryTimer?.Dispose();
        return removed;
    }

    private void RemoveChallenge(string challengeId)
    {
        if (_challenges.TryRemove(challengeId, out PendingChallenge? removed))
            removed.ExpiryTimer?.Dispose();
    }

    private AuthenticatedSession? FreshAuthenticationFor(string? accountProfileId, AppleAccountState? state)
    {
        AuthenticatedSession? authenticated = _lastAuthentication;
        if (authenticated is null || state is null || accountProfileId is null)
            return null;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (authenticated.AuthenticatedAt > now || now - authenticated.AuthenticatedAt > AuthenticationFreshness)
            return null;
        if (authenticated.AuthenticatedAt != state.AuthValidatedAt ||
            !string.Equals(AppleAccountIdentity.ProfileIdFor(authenticated.Session.AppleId), accountProfileId, StringComparison.Ordinal) ||
            !string.Equals(state.AccountProfileId, accountProfileId, StringComparison.Ordinal))
        {
            return null;
        }
        return authenticated;
    }

    private static Exception ClassifyAppleFailure(Exception exception, bool twoFactorSubmission)
    {
        if (exception is AppleUpstreamUnavailableException or AppleUpstreamRateLimitedException)
            return exception;

        HttpStatusCode? statusCode = exception switch
        {
            HttpRequestException request => request.StatusCode,
            GrandSlamException grandSlam => grandSlam.StatusCode,
            DeveloperServicesException developerServices => developerServices.StatusCode,
            _ => null,
        };
        if (statusCode == HttpStatusCode.TooManyRequests)
            return new AppleUpstreamRateLimitedException(exception);
        if (statusCode == HttpStatusCode.RequestTimeout ||
            statusCode is { } responseStatus && (int)responseStatus is >= 500 and <= 599 ||
            exception is HttpRequestException or IOException or TimeoutException ||
            exception is GrandSlamException { ErrorCode: null, StatusCode: null })
        {
            return new AppleUpstreamUnavailableException(exception);
        }
        return twoFactorSubmission
            ? new AppleTwoFactorInvalidException(exception)
            : new AppleAuthenticationFailedException(exception);
    }

    private string SecretCustody() => _credentialManagement.Source switch
    {
        AppleCredentialSources.Managed => "managed-encrypted-store",
        AppleCredentialSources.Keychain => "macos-keychain",
        _ => "environment-or-sops",
    };

    private string SecretCustodyLabel() => _credentialManagement.Source switch
    {
        AppleCredentialSources.Managed => "Sideport's encrypted credential store",
        AppleCredentialSources.Keychain => "the macOS login keychain",
        _ => "environment/SOPS secret custody",
    };

    private string MissingCredentialMessage() => _credentialManagement.Source switch
    {
        AppleCredentialSources.Managed => "No managed Apple credential is stored. Connect an Apple account over a protected connection.",
        AppleCredentialSources.Keychain => "Configure SIDEPORT_PERSONAL_APPLE_ID and store the password in the macOS login keychain " +
            "(security add-generic-password -s sideport-apple-pw -a <appleId> -w) before starting sign-in.",
        _ => "Configure SIDEPORT_PERSONAL_APPLE_ID and the matching SIDEPORT_APPLE_PW_* secret before starting sign-in.",
    };

    private void ClearChallengesFor(string appleId)
    {
        foreach ((string id, PendingChallenge challenge) in _challenges)
        {
            if (string.Equals(challenge.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
                RemoveChallenge(id);
        }
    }

    private void RemoveExpiredChallenges()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((string id, PendingChallenge challenge) in _challenges)
        {
            if (challenge.ExpiresAt <= now)
                RemoveChallenge(id);
        }
    }

    internal static string ValidateAppleId(string? appleId)
    {
        string value = NormalizeAppleId(appleId)
            ?? throw new ArgumentException("Apple ID is required.", nameof(appleId));
        if (value.Length > 320)
            throw new ArgumentException("Apple ID must be 320 characters or fewer.", nameof(appleId));
        return value;
    }

    internal static string ValidatePassword(string? password)
    {
        if (password is null || password.Length == 0)
            throw new ArgumentException("Password is required.", nameof(password));
        if (password.Length > 1024)
            throw new ArgumentException("Password must be 1024 characters or fewer.", nameof(password));
        return password;
    }

    private static string? NormalizeAppleId(string? appleId) =>
        string.IsNullOrWhiteSpace(appleId) ? null : appleId.Trim();

    private sealed record AuthenticatedSession(AppleSession Session, DateTimeOffset AuthenticatedAt)
    {
        public override string ToString() => "AuthenticatedSession { [REDACTED] }";
    }

    private sealed class PendingChallenge(
        string id,
        string appleId,
        AppleLoginChallenge challenge,
        string actor,
        string? candidatePassword,
        bool managedConnect,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        public string Id { get; } = id;
        public string AppleId { get; } = appleId;
        public AppleLoginChallenge Challenge { get; } = challenge;
        public string Actor { get; } = actor;
        public string? CandidatePassword { get; } = candidatePassword;
        public bool ManagedConnect { get; } = managedConnect;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public ITimer? ExpiryTimer { get; set; }

        public override string ToString() => "PendingChallenge { [REDACTED] }";
    }
}

internal sealed class AppleAuthenticationFailedException : InvalidOperationException
{
    public AppleAuthenticationFailedException() : base("Apple authentication failed.") { }
    public AppleAuthenticationFailedException(Exception innerException) : base("Apple authentication failed.", innerException) { }
}

internal sealed class AppleTwoFactorInvalidException : InvalidOperationException
{
    public AppleTwoFactorInvalidException() : base("Apple rejected the two-factor code.") { }
    public AppleTwoFactorInvalidException(Exception innerException) : base("Apple rejected the two-factor code.", innerException) { }
}

internal sealed class AppleChallengeNotFoundException : KeyNotFoundException;
internal sealed class AppleChallengeExpiredException : InvalidOperationException;
internal sealed class AppleUpstreamUnavailableException(Exception innerException)
    : Exception("Apple services are temporarily unavailable.", innerException);
internal sealed class AppleUpstreamRateLimitedException(Exception innerException)
    : Exception("Apple temporarily rate-limited this request.", innerException);
internal sealed class AppleCertificateInventoryUnavailableException(Exception innerException)
    : Exception("Apple's development-certificate inventory is temporarily unavailable.", innerException);
