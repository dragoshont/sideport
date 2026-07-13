using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

internal sealed record AppleAuthorityCutoverCoordinatorOptions(string JournalPath);

internal sealed class AppleAuthorityCutoverCoordinator(
    AppleAuthorityCutoverCoordinatorOptions options,
    IAppleCredentialManagement credentials,
    AppleAccountStateStore accountState,
    IAppRegistry registry,
    IDataProtectionProvider dataProtection)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Sideport.AppleAuthorityCutover.v1");

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await RecoverUnsafeAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<string?> ResolveCompletedReplacementAppleIdAsync(
        string currentAccountProfileId,
        string currentTeamId,
        string replacementAccountProfileId,
        string replacementTeamId,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RecoverUnsafeAsync(ct).ConfigureAwait(false);
            ManagedAppleCredentialMetadata? current = await credentials.ReadMetadataAsync(ct).ConfigureAwait(false);
            AppleAccountState? state = await accountState.ReadAsync(ct).ConfigureAwait(false);
            if (!string.Equals(current?.AccountProfileId, replacementAccountProfileId, StringComparison.Ordinal) ||
                !string.Equals(state?.AccountProfileId, replacementAccountProfileId, StringComparison.Ordinal) ||
                !string.Equals(state?.SelectedTeamId, replacementTeamId, StringComparison.Ordinal))
                return null;
            IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
            bool oldLineageRemains = registrations.Any(app =>
                string.Equals(AppleAccountIdentity.ProfileIdFor(app.AppleId), currentAccountProfileId, StringComparison.Ordinal) &&
                string.Equals(app.TeamId, currentTeamId, StringComparison.Ordinal));
            return oldLineageRemains ? null : current!.AppleId;
        }
        finally { _gate.Release(); }
    }

    public async Task CommitAsync(
        AppleAccountReplacementContext candidate,
        string selectedTeamId,
        string currentAppleId,
        string currentAccountProfileId,
        string currentTeamId,
        string actor,
        CancellationToken ct = default)
    {
        if (!candidate.Teams.Any(team => string.Equals(team.TeamId, selectedTeamId, StringComparison.Ordinal)))
            throw new AppleTeamNotReturnedException();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RecoverUnsafeAsync(ct).ConfigureAwait(false);
            ManagedAppleCredentialMetadata? current = await credentials.ReadMetadataAsync(ct).ConfigureAwait(false);
            if (current is null || !string.Equals(current.AccountProfileId, currentAccountProfileId, StringComparison.Ordinal))
                throw new SigningPreflightExpiredException();

            AppleAccountState? previousState = await accountState.ReadAsync(ct).ConfigureAwait(false);
            string replacementCredentialVersion = $"credential_{Guid.NewGuid():N}";
            var journal = new AppleAuthorityCutoverJournal(
                1,
                currentAppleId,
                currentAccountProfileId,
                currentTeamId,
                candidate.AppleId,
                AppleAccountIdentity.ProfileIdFor(candidate.AppleId),
                selectedTeamId,
                previousState,
                current.CredentialVersion,
                actor,
                replacementCredentialVersion,
                DateTimeOffset.UtcNow);
            await WriteJournalAsync(journal, ct).ConfigureAwait(false);

            try
            {
                await registry.RebindAppleAuthorityByProfileAsync(
                    currentAccountProfileId, currentTeamId, candidate.AppleId, selectedTeamId, ct).ConfigureAwait(false);
                AppleAccountState replacementState = AppleAccountStateStore.CreateAuthenticatedState(
                    candidate.AppleId, candidate.Teams, selectedTeamId, DateTimeOffset.UtcNow, actor);
                await accountState.ReplaceAsync(replacementState, ct).ConfigureAwait(false);
                await credentials.CommitReplacementAuthenticatedAsync(
                    candidate.AppleId, candidate.Password, actor, current.CredentialVersion, replacementCredentialVersion, ct).ConfigureAwait(false);
                DeleteJournal();
            }
            catch
            {
                await RecoverUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task RecoverUnsafeAsync(CancellationToken ct)
    {
        AppleAuthorityCutoverJournal? journal = await ReadJournalAsync(ct).ConfigureAwait(false);
        if (journal is null) return;
        ManagedAppleCredentialMetadata? current = await credentials.ReadMetadataAsync(ct).ConfigureAwait(false);
        bool replacementActive = !string.IsNullOrWhiteSpace(journal.ReplacementCredentialVersion) &&
            string.Equals(current?.AccountProfileId, journal.ReplacementAccountProfileId, StringComparison.Ordinal) &&
            string.Equals(current?.CredentialVersion, journal.ReplacementCredentialVersion, StringComparison.Ordinal);
        bool originalActive = string.Equals(current?.AccountProfileId, journal.CurrentAccountProfileId, StringComparison.Ordinal) &&
            string.Equals(current?.CredentialVersion, journal.CurrentCredentialVersion, StringComparison.Ordinal);
        if (replacementActive)
        {
            await registry.RebindAppleAuthorityByProfileAsync(
                journal.CurrentAccountProfileId, journal.CurrentTeamId,
                journal.ReplacementAppleId, journal.ReplacementTeamId, ct).ConfigureAwait(false);
            AppleAccountState? state = await accountState.ReadAsync(ct).ConfigureAwait(false);
            if (!string.Equals(state?.AccountProfileId, journal.ReplacementAccountProfileId, StringComparison.Ordinal) ||
                !string.Equals(state?.SelectedTeamId, journal.ReplacementTeamId, StringComparison.Ordinal))
                throw new AppleAuthorityCutoverRecoveryException("The replacement credential is active but its account state is incomplete.");
        }
        else if (originalActive)
        {
            await registry.RebindAppleAuthorityByProfileAsync(
                journal.ReplacementAccountProfileId, journal.ReplacementTeamId,
                journal.CurrentAppleId, journal.CurrentTeamId, ct).ConfigureAwait(false);
            await accountState.ReplaceAsync(journal.PreviousAccountState, ct).ConfigureAwait(false);
        }
        else
        {
            throw new AppleAuthorityCutoverRecoveryException("The active Apple credential does not match the cutover's original or replacement authority.");
        }
        DeleteJournal();
    }

    private async Task<AppleAuthorityCutoverJournal?> ReadJournalAsync(CancellationToken ct)
    {
        if (!File.Exists(options.JournalPath)) return null;
        try
        {
            string protectedJournal = await File.ReadAllTextAsync(options.JournalPath, ct).ConfigureAwait(false);
            string plaintext = _protector.Unprotect(protectedJournal);
            AppleAuthorityCutoverJournal? journal = JsonSerializer.Deserialize<AppleAuthorityCutoverJournal>(plaintext, JsonOptions);
            if (journal is null || journal.SchemaVersion != 1 || string.IsNullOrWhiteSpace(journal.CurrentAccountProfileId) || string.IsNullOrWhiteSpace(journal.ReplacementAccountProfileId))
                throw new InvalidDataException("The Apple authority cutover journal is invalid.");
            return journal;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new AppleAuthorityCutoverRecoveryException("The Apple authority cutover journal is unavailable.", ex); }
    }

    private async Task WriteJournalAsync(AppleAuthorityCutoverJournal journal, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(options.JournalPath);
        if (!string.IsNullOrWhiteSpace(directory)) PrivateAppleStoreFiles.EnsureDirectory(directory);
        string temp = $"{options.JournalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string protectedJournal = _protector.Protect(JsonSerializer.Serialize(journal, JsonOptions));
            await File.WriteAllTextAsync(temp, protectedJournal, ct).ConfigureAwait(false);
            PrivateAppleStoreFiles.RestrictFile(temp);
            File.Move(temp, options.JournalPath, true);
            PrivateAppleStoreFiles.RestrictFile(options.JournalPath);
        }
        catch { try { File.Delete(temp); } catch { } throw; }
    }

    private void DeleteJournal()
    {
        try { if (File.Exists(options.JournalPath)) File.Delete(options.JournalPath); }
        catch (Exception ex) { throw new AppleAuthorityCutoverRecoveryException("The completed Apple authority cutover journal could not be removed.", ex); }
    }

    private sealed record AppleAuthorityCutoverJournal(
        int SchemaVersion,
        string CurrentAppleId,
        string CurrentAccountProfileId,
        string CurrentTeamId,
        string ReplacementAppleId,
        string ReplacementAccountProfileId,
        string ReplacementTeamId,
        AppleAccountState? PreviousAccountState,
        string CurrentCredentialVersion,
        string Actor,
        string? ReplacementCredentialVersion,
        DateTimeOffset CreatedAt);
}

internal sealed class AppleAuthorityCutoverRecoveryService(AppleAuthorityCutoverCoordinator coordinator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => coordinator.RecoverAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class AppleAuthorityCutoverRecoveryException : Exception
{
    public AppleAuthorityCutoverRecoveryException(string message) : base(message) { }
    public AppleAuthorityCutoverRecoveryException(string message, Exception inner) : base(message, inner) { }
}
