using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

internal sealed class SigningCutoverService(
    IPersonalAppleAccess personalApple,
    ISigningIdentityProvider signingIdentity,
    IAppRegistry registry,
    OperationStore operations,
    SignerAuthorityGate signerAuthorityGate,
    AppleAccountReplacementCandidateService replacementCandidates,
    AppleAuthorityCutoverCoordinator authorityCutover,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Authorization> _preflights = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PersonalAppleSigningPreflightDto> PreflightAsync(
        PersonalAppleSigningPreflightRequest request,
        string actor,
        CancellationToken ct = default)
    {
        PersonalAppleSigningPreflightContext context;
        AppleAccountReplacementContext? replacement = null;
        if (!string.IsNullOrWhiteSpace(request.CandidateId))
        {
            string currentProfileId = Required(request.CurrentAccountProfileId, nameof(request.CurrentAccountProfileId));
            PersonalAppleInstallContext current = await personalApple.ResolveFreshInstallContextAsync(currentProfileId, ct).ConfigureAwait(false);
            replacement = replacementCandidates.Resolve(request.CandidateId, actor);
            if (!replacement.Teams.Any(team => string.Equals(team.TeamId, request.TeamId, StringComparison.Ordinal)))
                throw new AppleTeamNotReturnedException();
            IReadOnlyList<AppleDevelopmentCertificate> certificates = await replacementCandidates.ListCertificatesAsync(replacement, request.TeamId, ct).ConfigureAwait(false);
            context = new PersonalAppleSigningPreflightContext(
                new PersonalAppleInstallContext(replacement.AppleId, AppleAccountIdentity.ProfileIdFor(replacement.AppleId), request.TeamId, _time.GetUtcNow()),
                current.AppleId,
                current.AccountProfileId,
                current.TeamId,
                certificates,
                new SigningIdentityInspection("unknown", null, null),
                replacement.Session);
        }
        else
        {
            context = await personalApple.ResolveSigningPreflightAsync(request, ct).ConfigureAwait(false);
        }
        SigningIdentityInspection local = await signingIdentity.InspectAsync(
            context.Install.AppleId, context.Install.TeamId, ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        AppRegistration[] affected = registrations.Where(app =>
            string.Equals(AppleAccountIdentity.ProfileIdFor(app.AppleId), context.CurrentAccountProfileId, StringComparison.Ordinal) &&
            string.Equals(app.TeamId, context.CurrentTeamId, StringComparison.Ordinal)).ToArray();
        string[] ids = context.Certificates.Select(certificate => certificate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string inventoryVersion = InventoryVersion(ids, context.Certificates);
        string impact = string.Equals(local.State, "reusable", StringComparison.Ordinal)
            ? "reuse"
            : ids.Length == 0 ? "mint" : "replace-existing";
        DateTimeOffset now = _time.GetUtcNow();
        string preflightId = $"signing_preflight_{Guid.NewGuid():N}";
        var authorization = new Authorization(
            preflightId, now.Add(Lifetime), context, local, ids, inventoryVersion, impact,
            affected.Length, affected.Select(app => app.DeviceUdid).Distinct(StringComparer.OrdinalIgnoreCase).Count(), replacement);
        _preflights[preflightId] = authorization;
        return Project(authorization);
    }

    public async Task<(OperationRecordDto Record, bool Created)> CutoverAsync(
        PersonalAppleCutoverRequest request,
        OperationActorDto actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string idempotencyKey = Required(request.IdempotencyKey, nameof(request.IdempotencyKey));
        OperationRecordDto? replay = await operations.FindByActorAndIdempotencyAsync("signer-cutover", actor, idempotencyKey, ct).ConfigureAwait(false);
        if (replay?.Status == "recovery-required" && replay.SigningCutoverIntent is { ReplacesAccount: true } completedIntent)
        {
            EnsureReplayAcknowledgementsMatch(completedIntent, request);
            string? completedAppleId = await authorityCutover.ResolveCompletedReplacementAppleIdAsync(
                completedIntent.CurrentAccountProfileId,
                completedIntent.CurrentTeamId,
                completedIntent.AccountProfileId,
                completedIntent.TeamId,
                ct).ConfigureAwait(false);
            if (completedAppleId is not null)
            {
                SigningIdentityInspection completedIdentity = await signingIdentity.InspectAsync(completedAppleId, completedIntent.TeamId, ct).ConfigureAwait(false);
                if (string.Equals(completedIdentity.State, "reusable", StringComparison.Ordinal))
                {
                    DateTimeOffset completedAt = _time.GetUtcNow();
                    OperationRecordDto completed = replay with
                    {
                        Status = "succeeded",
                        UpdatedAt = completedAt,
                        CompletedAt = completedAt,
                        Error = null,
                        Retryable = false,
                        Result = new OperationResultDto(true, null, completedIdentity.ExpiresAt, null),
                        Stages = replay.Stages.Select(stage => stage with
                        {
                            Status = "succeeded",
                            StartedAt = stage.StartedAt ?? replay.StartedAt ?? completedAt,
                            CompletedAt = completedAt,
                            Error = null,
                        }).ToArray(),
                    };
                    return (await operations.UpdateAsync(completed, ct).ConfigureAwait(false), false);
                }
            }
        }
        if (replay is not null && replay.Status != "recovery-required")
        {
            EnsureReplayMatches(replay, request);
            return (replay, false);
        }
        string preflightId = Required(request.PreflightId, nameof(request.PreflightId));
        if (!_preflights.TryGetValue(preflightId, out Authorization? authorization) || authorization.ExpiresAt <= _time.GetUtcNow())
        {
            if (replay?.Status != "recovery-required" || replay.SigningCutoverIntent is not { } intent ||
                !string.Equals(intent.PreflightId, preflightId, StringComparison.Ordinal))
                throw new SigningPreflightExpiredException();
            PersonalAppleSigningPreflightContext resolved = await personalApple.ResolveSigningPreflightAsync(
                new PersonalAppleSigningPreflightRequest(intent.AccountProfileId, intent.TeamId), ct).ConfigureAwait(false);
            PersonalAppleSigningPreflightContext context = intent.ReplacesAccount
                ? resolved with
                {
                    CurrentAccountProfileId = intent.CurrentAccountProfileId,
                    CurrentTeamId = intent.CurrentTeamId,
                }
                : resolved;
            IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
            authorization = new Authorization(
                intent.PreflightId,
                _time.GetUtcNow().Add(Lifetime),
                context,
                await signingIdentity.InspectAsync(context.Install.AppleId, context.Install.TeamId, ct).ConfigureAwait(false),
                intent.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                intent.InventoryVersion,
                intent.AcknowledgedImpactCodes.Single(),
                registrations.Count(app => string.Equals(AppleAccountIdentity.ProfileIdFor(app.AppleId), intent.CurrentAccountProfileId, StringComparison.Ordinal) && string.Equals(app.TeamId, intent.CurrentTeamId, StringComparison.Ordinal)),
                registrations.Where(app => string.Equals(AppleAccountIdentity.ProfileIdFor(app.AppleId), intent.CurrentAccountProfileId, StringComparison.Ordinal) && string.Equals(app.TeamId, intent.CurrentTeamId, StringComparison.Ordinal)).Select(app => app.DeviceUdid).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Replacement: null);
            _preflights[preflightId] = authorization;
        }
        if (replay?.Status == "recovery-required" && replay.SigningCutoverIntent is { ReplacesAccount: true } recoveryIntent)
        {
            if (authorization.Replacement is null ||
                !string.Equals(authorization.Context.CurrentAccountProfileId, recoveryIntent.CurrentAccountProfileId, StringComparison.Ordinal) ||
                !string.Equals(authorization.Context.CurrentTeamId, recoveryIntent.CurrentTeamId, StringComparison.Ordinal) ||
                !string.Equals(authorization.Context.Install.AccountProfileId, recoveryIntent.AccountProfileId, StringComparison.Ordinal) ||
                !string.Equals(authorization.Context.Install.TeamId, recoveryIntent.TeamId, StringComparison.Ordinal) ||
                !string.Equals(request.InventoryVersion, recoveryIntent.InventoryVersion, StringComparison.Ordinal) ||
                !request.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(recoveryIntent.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal) ||
                !request.AcknowledgedImpactCodes.OrderBy(code => code, StringComparer.Ordinal).SequenceEqual(recoveryIntent.AcknowledgedImpactCodes.OrderBy(code => code, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new SigningIdempotencyTargetConflictException();
            authorization = authorization with
            {
                CertificateIds = recoveryIntent.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                InventoryVersion = recoveryIntent.InventoryVersion,
                Impact = recoveryIntent.AcknowledgedImpactCodes.Single(),
            };
            _preflights[authorization.PreflightId] = authorization;
        }

        if (!string.Equals(request.InventoryVersion, authorization.InventoryVersion, StringComparison.Ordinal) ||
            !request.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(authorization.CertificateIds, StringComparer.Ordinal) ||
            !request.AcknowledgedImpactCodes.SequenceEqual([authorization.Impact], StringComparer.Ordinal))
            throw new SigningAcknowledgementMismatchException();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (authorization.Replacement is not null)
            {
                authorization = authorization with
                {
                    Replacement = replacementCandidates.Consume(authorization.Replacement.CandidateId, actor.DisplayName),
                };
                _preflights[authorization.PreflightId] = authorization;
            }
            replay = await operations.FindByActorAndIdempotencyAsync("signer-cutover", actor, idempotencyKey, ct).ConfigureAwait(false);
            if (replay is not null && replay.Status != "recovery-required")
            {
                EnsureReplayMatches(replay, request);
                return (replay, false);
            }
            DateTimeOffset now = _time.GetUtcNow();
            string operationId = replay?.OperationId ?? $"op_{now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
            var record = new OperationRecordDto(
                operationId, "signer-cutover", "running", now, now, now, null, actor, idempotencyKey, 1,
                new OperationTargetDto(null, null, TeamId: authorization.Context.Install.TeamId, Kind: "apple-signer", AccountProfileId: authorization.Context.Install.AccountProfileId),
                [
                    new("preflight", "Preflight", "succeeded", now, now, "Exact certificate impact confirmed."),
                    new("revalidate-certificate-inventory", "Recheck Apple certificates", "running", now, null, "Rechecking the exact Apple inventory."),
                    new("revoke-acknowledged-certificates", "Replace acknowledged certificates", "pending", null, null, "Waiting for inventory revalidation."),
                    new("mint-certificate", "Create signing identity", "pending", null, null, "Waiting for exact replacement."),
                    new("persist-identity", "Save signing identity", "pending", null, null, "Waiting for certificate creation."),
                    new("verify-identity", "Verify signing identity", "pending", null, null, "Waiting for persistence."),
                ], null, null, false, false, false, operationId,
                SigningCutoverIntent: new SigningCutoverIntentDto(
                    authorization.Context.CurrentAccountProfileId,
                    authorization.Context.CurrentTeamId,
                    authorization.Context.Install.AccountProfileId,
                    authorization.Context.Install.TeamId,
                    authorization.PreflightId,
                    authorization.InventoryVersion,
                    authorization.CertificateIds,
                    [authorization.Impact],
                    authorization.LocalIdentity.State,
                    authorization.LocalIdentity.SerialSuffix,
                    authorization.Replacement is not null));
            bool created;
            if (replay is null)
                (record, created) = await operations.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            else
            {
                record = record with { CreatedAt = replay.CreatedAt, Attempt = replay.Attempt + 1, ParentOperationId = replay.ParentOperationId };
                record = await operations.UpdateAsync(record, ct).ConfigureAwait(false);
                created = false;
            }
            if (!created && replay is null) return (record, false);
            try
            {
                bool recovery = replay?.Status == "recovery-required";
                SigningIdentityInspection result = await signerAuthorityGate.RunAsync(async gateCt => authorization.Impact == "reuse"
                    ? await FinalizeReuseAsync(authorization, actor, gateCt).ConfigureAwait(false)
                    : await signingIdentity.ReplaceAndFinalizeAsync(
                        authorization.Context.Session,
                        authorization.Context.Install.TeamId,
                        authorization.CertificateIds,
                        allowPersistedIdentityRecovery: recovery,
                        async (_, finalizeCt) =>
                            await FinalizeAuthorityAsync(authorization, actor, finalizeCt).ConfigureAwait(false),
                        gateCt).ConfigureAwait(false), ct).ConfigureAwait(false);
                DateTimeOffset completed = _time.GetUtcNow();
                record = record with
                {
                    Status = "succeeded", UpdatedAt = completed, CompletedAt = completed,
                    Stages = record.Stages.Select(stage => stage with { Status = "succeeded", StartedAt = stage.StartedAt ?? now, CompletedAt = completed, Message = stage.Id == "verify-identity" ? "The new signing identity was verified." : stage.Message }).ToArray(),
                    Result = new OperationResultDto(true, null, result.ExpiresAt, null),
                };
                _preflights.TryRemove(authorization.PreflightId, out _);
                return (await operations.UpdateAsync(record, ct).ConfigureAwait(false), created);
            }
            catch (SigningReplacementInventoryChangedException)
            {
                DateTimeOffset failed = _time.GetUtcNow();
                OperationIssueDto error = new("signing-preflight-stale", "Apple's certificate inventory changed. Review the current impact again.");
                record = record with { Status = "blocked", UpdatedAt = failed, CompletedAt = failed, Error = error, Stages = record.Stages.Select(stage => stage.Id == "revalidate-certificate-inventory" ? stage with { Status = "blocked", CompletedAt = failed, Error = error, Message = error.Message } : stage).ToArray() };
                return (await operations.UpdateAsync(record, ct).ConfigureAwait(false), created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                DateTimeOffset failed = _time.GetUtcNow();
                OperationIssueDto error = new("signer-cutover-recovery-required", "Signing replacement stopped after authorization. Retry only this saved cutover after Apple access is restored.");
                record = record with
                {
                    Status = "recovery-required",
                    UpdatedAt = failed,
                    CompletedAt = failed,
                    Error = error,
                    Retryable = true,
                    Stages = record.Stages.Select(stage => stage.Status is "running" or "pending" ? stage with
                    {
                        Status = stage.Status == "running" ? "failed" : stage.Status,
                        CompletedAt = stage.Status == "running" ? failed : stage.CompletedAt,
                        Error = stage.Status == "running" ? error : stage.Error,
                    } : stage).ToArray(),
                };
                return (await operations.UpdateAsync(record, ct).ConfigureAwait(false), created);
            }
        }
        finally { _gate.Release(); }
    }

    private PersonalAppleSigningPreflightDto Project(Authorization value) => new(
        value.PreflightId, value.ExpiresAt, value.Context.Install.AccountProfileId, value.Context.Install.TeamId,
        value.LocalIdentity,
        value.Context.Certificates.Select(certificate => new PersonalAppleSigningCertificateDto(certificate.Id, Suffix(certificate.SerialNumber), certificate.ExpiresAt)).ToArray(),
        value.Impact, value.Impact == "replace-existing", value.InventoryVersion,
        value.RegistrationCount, value.DeviceCount, value.RegistrationCount);
    private static string InventoryVersion(string[] ids, IReadOnlyList<AppleDevelopmentCertificate> certificates) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', certificates.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => $"{c.Id}:{c.SerialNumber}:{c.ExpiresAt:O}")))))}";
    private static string? Suffix(string value) => string.IsNullOrWhiteSpace(value) ? null : value[^Math.Min(4, value.Length)..];
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim();
    private async Task<SigningIdentityInspection> FinalizeReuseAsync(Authorization authorization, OperationActorDto actor, CancellationToken ct)
    {
        await FinalizeAuthorityAsync(authorization, actor, ct).ConfigureAwait(false);
        return authorization.LocalIdentity;
    }
    private async Task FinalizeAuthorityAsync(Authorization authorization, OperationActorDto actor, CancellationToken ct)
    {
        if (authorization.Replacement is not null)
        {
            await authorityCutover.CommitAsync(
                authorization.Replacement,
                authorization.Context.Install.TeamId,
                authorization.Context.CurrentAppleId,
                authorization.Context.CurrentAccountProfileId,
                authorization.Context.CurrentTeamId,
                actor.DisplayName,
                ct).ConfigureAwait(false);
            return;
        }
        await registry.RebindAppleAuthorityByProfileAsync(
            authorization.Context.CurrentAccountProfileId,
            authorization.Context.CurrentTeamId,
            authorization.Context.Install.AppleId,
            authorization.Context.Install.TeamId,
            ct).ConfigureAwait(false);
        await personalApple.SelectTeamAsync(
            new PersonalAppleTeamSelectionRequest(authorization.Context.Install.AccountProfileId, authorization.Context.Install.TeamId),
            actor.DisplayName,
            ct).ConfigureAwait(false);
    }
    private sealed record Authorization(string PreflightId, DateTimeOffset ExpiresAt, PersonalAppleSigningPreflightContext Context, SigningIdentityInspection LocalIdentity, string[] CertificateIds, string InventoryVersion, string Impact, int RegistrationCount, int DeviceCount, AppleAccountReplacementContext? Replacement);
    private static void EnsureReplayMatches(OperationRecordDto replay, PersonalAppleCutoverRequest request)
    {
        SigningCutoverIntentDto? intent = replay.SigningCutoverIntent;
        if (intent is null ||
            !string.Equals(intent.PreflightId, request.PreflightId, StringComparison.Ordinal))
            throw new SigningIdempotencyTargetConflictException();
        EnsureReplayAcknowledgementsMatch(intent, request);
    }

    private static void EnsureReplayAcknowledgementsMatch(SigningCutoverIntentDto intent, PersonalAppleCutoverRequest request)
    {
        if (!string.Equals(intent.InventoryVersion, request.InventoryVersion, StringComparison.Ordinal) ||
            !request.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(intent.AcknowledgedCertificateIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal) ||
            !request.AcknowledgedImpactCodes.OrderBy(code => code, StringComparer.Ordinal).SequenceEqual(intent.AcknowledgedImpactCodes.OrderBy(code => code, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new SigningIdempotencyTargetConflictException();
    }
}

public sealed class SigningPreflightExpiredException : Exception;
public sealed class SigningAcknowledgementMismatchException : Exception;
public sealed class SigningIdempotencyTargetConflictException : Exception;
public sealed class SigningAccountReauthenticationRequiredException : Exception;
