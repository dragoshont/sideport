using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.AppleAccess;

internal static class AppleCredentialTransportPolicy
{
    public static bool IsAllowed(
        bool isHttps,
        IPAddress? localAddress,
        IPAddress? remoteAddress,
        bool allowInsecureLoopback)
    {
        if (isHttps)
            return true;
        return allowInsecureLoopback &&
               localAddress is not null && IPAddress.IsLoopback(localAddress) &&
               remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
    }
}

internal static class AppleCredentialSources
{
    public const string Environment = "environment";
    public const string Keychain = "keychain";
    public const string Managed = "managed";

    public static string Normalize(string? source)
    {
        string value = string.IsNullOrWhiteSpace(source) ? Environment : source.Trim().ToLowerInvariant();
        return value switch
        {
            Environment or Keychain or Managed => value,
            _ => throw new InvalidOperationException(
                $"Unsupported Sideport:Apple:CredentialSource '{value}'. Use managed, environment, or keychain."),
        };
    }
}

internal static class AppleAccountIdentity
{
    public static string ProfileIdFor(string appleId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(appleId.Trim().ToLowerInvariant()));
        return $"acct_{Convert.ToHexStringLower(hash)[..20]}";
    }

    public static string Redact(string? appleId)
    {
        if (string.IsNullOrWhiteSpace(appleId)) return "***";
        int at = appleId.IndexOf('@');
        if (at <= 1) return "***";
        return $"{appleId[0]}***{appleId[at..]}";
    }

    public static string RequireActor(string? actor)
    {
        string value = actor?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 512)
            throw new ArgumentException("A verified actor is required.", nameof(actor));
        return value;
    }
}

internal sealed record ManagedAppleCredentialStoreOptions(
    string DirectoryPath,
    string KeyRingDirectoryPath)
{
    public string CredentialPath => Path.Combine(DirectoryPath, "credential.json");
}

internal sealed record AppleAccountStateStoreOptions(string StatePath);

internal sealed record ManagedAppleCredentialMetadata(
    string AppleId,
    string AccountProfileId,
    string CredentialVersion,
    DateTimeOffset UpdatedAt,
    string UpdatedByActor);

internal sealed record ManagedAppleCredentialCommit(
    ManagedAppleCredentialMetadata Metadata,
    bool Created);

internal interface IAppleCredentialManagement
{
    string Source { get; }
    bool SupportsEntry { get; }
    Task<ManagedAppleCredentialMetadata?> ReadMetadataAsync(CancellationToken ct = default);
    Task<ManagedAppleCredentialCommit> CommitAuthenticatedAsync(
        string appleId,
        string password,
        string actor,
        CancellationToken ct = default);
    Task<ManagedAppleCredentialCommit> CommitReplacementAuthenticatedAsync(
        string appleId,
        string password,
        string actor,
        string? expectedCredentialVersion = null,
        string? replacementCredentialVersion = null,
        CancellationToken ct = default);
}

internal sealed class ReadOnlyAppleCredentialManagement(string source) : IAppleCredentialManagement
{
    public string Source { get; } = AppleCredentialSources.Normalize(source);
    public bool SupportsEntry => false;

    public Task<ManagedAppleCredentialMetadata?> ReadMetadataAsync(CancellationToken ct = default) =>
        Task.FromResult<ManagedAppleCredentialMetadata?>(null);

    public Task<ManagedAppleCredentialCommit> CommitAuthenticatedAsync(
        string appleId,
        string password,
        string actor,
        CancellationToken ct = default) =>
        throw new AppleCredentialSourceReadOnlyException();
    public Task<ManagedAppleCredentialCommit> CommitReplacementAuthenticatedAsync(string appleId, string password, string actor, string? expectedCredentialVersion = null, string? replacementCredentialVersion = null, CancellationToken ct = default) =>
        throw new AppleCredentialSourceReadOnlyException();
}

internal sealed class ManagedAppleCredentialStore : IAppleCredentialProvider, IAppleCredentialManagement
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ManagedAppleCredentialStoreOptions _options;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ManagedAppleCredentialStore(
        ManagedAppleCredentialStoreOptions options,
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _options = options;
        PrivateAppleStoreFiles.EnsureDirectory(_options.DirectoryPath);
        PrivateAppleStoreFiles.HardenFiles(_options.KeyRingDirectoryPath);
        _protector = dataProtectionProvider.CreateProtector("Sideport.AppleCredential.v1");
    }

    public string Source => AppleCredentialSources.Managed;
    public bool SupportsEntry => true;

    public async Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ManagedAppleCredentialPayload? payload = await ReadPayloadUnsafeAsync(ct).ConfigureAwait(false);
            return payload is not null && string.Equals(payload.AppleId, appleId, StringComparison.OrdinalIgnoreCase)
                ? payload.Password
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ManagedAppleCredentialMetadata?> ReadMetadataAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            (ManagedAppleCredentialEnvelope? envelope, ManagedAppleCredentialPayload? payload) =
                await ReadUnsafeAsync(ct).ConfigureAwait(false);
            return envelope is null || payload is null
                ? null
                : Metadata(envelope, payload);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ManagedAppleCredentialCommit> CommitAuthenticatedAsync(
        string appleId,
        string password,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        ArgumentNullException.ThrowIfNull(password);
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            (ManagedAppleCredentialEnvelope? existingEnvelope, ManagedAppleCredentialPayload? existingPayload) =
                await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (existingPayload is not null &&
                !string.Equals(existingPayload.AppleId, appleId, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppleAccountReplacementRequiresCutoverException();
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var payload = new ManagedAppleCredentialPayload
            {
                AppleId = appleId,
                Password = password,
                UpdatedByActor = verifiedActor,
            };
            string protectedPayload;
            try
            {
                protectedPayload = _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
                PrivateAppleStoreFiles.HardenFiles(_options.KeyRingDirectoryPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new AppleCredentialStoreException("The managed Apple credential could not be encrypted.", ex);
            }

            var envelope = new ManagedAppleCredentialEnvelope(
                SchemaVersion,
                 $"credential_{Guid.NewGuid():N}",
                protectedPayload,
                now);
            await WriteEnvelopeUnsafeAsync(envelope, ct).ConfigureAwait(false);
            return new ManagedAppleCredentialCommit(Metadata(envelope, payload), existingEnvelope is null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ManagedAppleCredentialCommit> CommitReplacementAuthenticatedAsync(
        string appleId,
        string password,
        string actor,
        string? expectedCredentialVersion = null,
        string? replacementCredentialVersion = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        ArgumentNullException.ThrowIfNull(password);
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            (ManagedAppleCredentialEnvelope? existingEnvelope, _) = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (expectedCredentialVersion is not null && !string.Equals(existingEnvelope?.CredentialVersion, expectedCredentialVersion, StringComparison.Ordinal))
                throw new AppleCredentialVersionConflictException();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var payload = new ManagedAppleCredentialPayload { AppleId = appleId, Password = password, UpdatedByActor = verifiedActor };
            string protectedPayload = _protector.Protect(JsonSerializer.Serialize(payload, JsonOptions));
            PrivateAppleStoreFiles.HardenFiles(_options.KeyRingDirectoryPath);
            var envelope = new ManagedAppleCredentialEnvelope(SchemaVersion, replacementCredentialVersion ?? $"credential_{Guid.NewGuid():N}", protectedPayload, now);
            await WriteEnvelopeUnsafeAsync(envelope, ct).ConfigureAwait(false);
            return new ManagedAppleCredentialCommit(Metadata(envelope, payload), existingEnvelope is null);
        }
        catch (OperationCanceledException) { throw; }
        catch (AppleCredentialStoreException) { throw; }
        catch (Exception ex) { throw new AppleCredentialStoreException("The managed Apple credential could not be replaced.", ex); }
        finally { _gate.Release(); }
    }

    private async Task<ManagedAppleCredentialPayload?> ReadPayloadUnsafeAsync(CancellationToken ct)
    {
        (_, ManagedAppleCredentialPayload? payload) = await ReadUnsafeAsync(ct).ConfigureAwait(false);
        return payload;
    }

    private async Task<(ManagedAppleCredentialEnvelope? Envelope, ManagedAppleCredentialPayload? Payload)> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_options.CredentialPath))
            return (null, null);

        try
        {
            await using FileStream stream = File.OpenRead(_options.CredentialPath);
            ManagedAppleCredentialEnvelope? envelope = await JsonSerializer.DeserializeAsync<ManagedAppleCredentialEnvelope>(stream, JsonOptions, ct).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(envelope.ProtectedPayload))
                throw new InvalidDataException("The managed credential envelope is invalid.");

            string plaintext = _protector.Unprotect(envelope.ProtectedPayload);
            ManagedAppleCredentialPayload? payload = JsonSerializer.Deserialize<ManagedAppleCredentialPayload>(plaintext, JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AppleId) || payload.Password is null)
                throw new InvalidDataException("The managed credential payload is invalid.");
            return (envelope, payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppleCredentialStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppleCredentialStoreException("The managed Apple credential store is unavailable.", ex);
        }
    }

    private async Task WriteEnvelopeUnsafeAsync(ManagedAppleCredentialEnvelope envelope, CancellationToken ct)
    {
        PrivateAppleStoreFiles.EnsureDirectory(_options.DirectoryPath);
        string tempPath = $"{_options.CredentialPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            PrivateAppleStoreFiles.RestrictFile(tempPath);
            File.Move(tempPath, _options.CredentialPath, overwrite: true);
            PrivateAppleStoreFiles.RestrictFile(_options.CredentialPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            throw new AppleCredentialStoreException("The managed Apple credential could not be saved.", ex);
        }
    }

    private static ManagedAppleCredentialMetadata Metadata(
        ManagedAppleCredentialEnvelope envelope,
        ManagedAppleCredentialPayload payload) =>
        new(
            payload.AppleId,
            AppleAccountIdentity.ProfileIdFor(payload.AppleId),
            envelope.CredentialVersion,
            envelope.UpdatedAt,
            string.IsNullOrWhiteSpace(payload.UpdatedByActor) ? "legacy:unknown" : payload.UpdatedByActor);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ManagedAppleCredentialEnvelope(
        int SchemaVersion,
        string CredentialVersion,
        string ProtectedPayload,
        DateTimeOffset UpdatedAt);

    private sealed class ManagedAppleCredentialPayload
    {
        public string AppleId { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string UpdatedByActor { get; init; } = "legacy:unknown";

        public override string ToString() => "ManagedAppleCredentialPayload { [REDACTED] }";
    }
}

internal sealed class AppleAccountStateStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly AppleAccountStateStoreOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppleAccountStateStore(AppleAccountStateStoreOptions options)
    {
        _options = options;
        string? directory = Path.GetDirectoryName(options.StatePath);
        if (!string.IsNullOrWhiteSpace(directory))
            PrivateAppleStoreFiles.EnsureDirectory(directory);
    }

    public async Task<AppleAccountState?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static AppleAccountState CreateAuthenticatedState(
        string appleId,
        IReadOnlyList<AppleTeam> teams,
        string? selectedTeamId,
        DateTimeOffset authenticatedAt,
        string actor)
    {
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        AppleAccountTeamState[] teamStates = teams
            .Where(team => !string.IsNullOrWhiteSpace(team.TeamId))
            .Select(team => new AppleAccountTeamState(team.TeamId.Trim(), team.Name, team.Type))
            .GroupBy(team => team.TeamId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (selectedTeamId is not null && !teamStates.Any(team => string.Equals(team.TeamId, selectedTeamId, StringComparison.Ordinal)))
            throw new AppleTeamNotReturnedException();
        return new AppleAccountState(
            SchemaVersion,
            AppleAccountIdentity.ProfileIdFor(appleId),
            AppleAccountIdentity.Redact(appleId),
            teamStates,
            authenticatedAt,
            selectedTeamId,
            selectedTeamId is null ? null : authenticatedAt,
            verifiedActor,
            selectedTeamId is null ? null : verifiedActor);
    }

    public async Task ReplaceAsync(AppleAccountState? state, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state is null)
            {
                if (File.Exists(_options.StatePath)) File.Delete(_options.StatePath);
                return;
            }
            await WriteUnsafeAsync(state, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<AppleAccountState> RecordAuthenticationAsync(
        string appleId,
        IReadOnlyList<AppleTeam> teams,
        DateTimeOffset authenticatedAt,
        string actor,
        CancellationToken ct = default)
    {
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        string accountProfileId = AppleAccountIdentity.ProfileIdFor(appleId);
        AppleAccountTeamState[] teamStates = teams
            .Where(team => !string.IsNullOrWhiteSpace(team.TeamId))
            .Select(team => new AppleAccountTeamState(team.TeamId.Trim(), team.Name, team.Type))
            .GroupBy(team => team.TeamId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppleAccountState? existing = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            bool sameProfile = string.Equals(existing?.AccountProfileId, accountProfileId, StringComparison.Ordinal);
            string? selectedTeamId = sameProfile && teamStates.Any(team => string.Equals(team.TeamId, existing!.SelectedTeamId, StringComparison.Ordinal))
                ? existing!.SelectedTeamId
                : teamStates.Length == 1
                    ? teamStates[0].TeamId
                    : null;
            bool automaticallySelectedOnlyTeam = selectedTeamId is not null &&
                (!sameProfile || !string.Equals(existing?.SelectedTeamId, selectedTeamId, StringComparison.Ordinal));
            var updated = new AppleAccountState(
                SchemaVersion,
                accountProfileId,
                AppleAccountIdentity.Redact(appleId),
                teamStates,
                authenticatedAt,
                selectedTeamId,
                selectedTeamId is null
                    ? null
                    : automaticallySelectedOnlyTeam
                        ? authenticatedAt
                        : existing!.TeamValidatedAt,
                verifiedActor,
                selectedTeamId is null
                    ? null
                    : automaticallySelectedOnlyTeam
                        ? verifiedActor
                        : existing!.TeamSelectedByActor);
            await WriteUnsafeAsync(updated, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppleAccountState> SelectTeamAsync(
        string accountProfileId,
        string teamId,
        DateTimeOffset now,
        TimeSpan authenticationFreshness,
        string actor,
        CancellationToken ct = default)
    {
        string verifiedActor = AppleAccountIdentity.RequireActor(actor);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AppleAccountState? existing = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (existing is null || !string.Equals(existing.AccountProfileId, accountProfileId, StringComparison.Ordinal))
                throw new AppleAccountProfileNotFoundException();
            if (now - existing.AuthValidatedAt > authenticationFreshness)
                throw new AppleTeamSelectionStaleException();
            if (!existing.Teams.Any(team => string.Equals(team.TeamId, teamId, StringComparison.Ordinal)))
                throw new AppleTeamNotReturnedException();

            AppleAccountState updated = existing with
            {
                SelectedTeamId = teamId,
                TeamValidatedAt = now,
                TeamSelectedByActor = verifiedActor,
            };
            await WriteUnsafeAsync(updated, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppleAccountState?> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_options.StatePath))
            return null;
        try
        {
            await using FileStream stream = File.OpenRead(_options.StatePath);
            AppleAccountState? state = await JsonSerializer.DeserializeAsync<AppleAccountState>(stream, JsonOptions, ct).ConfigureAwait(false);
            if (state is null ||
                state.SchemaVersion != SchemaVersion ||
                string.IsNullOrWhiteSpace(state.AccountProfileId) ||
                string.IsNullOrWhiteSpace(state.AppleIdHint) ||
                string.IsNullOrWhiteSpace(state.LastAuthenticatedByActor) ||
                state.AuthValidatedAt == default ||
                state.Teams is null ||
                state.Teams.Any(team => team is null || string.IsNullOrWhiteSpace(team.TeamId)) ||
                (state.SelectedTeamId is not null &&
                 !state.Teams.Any(team => string.Equals(team.TeamId, state.SelectedTeamId, StringComparison.Ordinal))) ||
                (state.SelectedTeamId is not null && string.IsNullOrWhiteSpace(state.TeamSelectedByActor)) ||
                (state.SelectedTeamId is null) != (state.TeamValidatedAt is null))
                throw new InvalidDataException("The Apple account state is invalid.");
            return state;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppleAccountStateStoreException("The Apple account state store is unavailable.", ex);
        }
    }

    private async Task WriteUnsafeAsync(AppleAccountState state, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_options.StatePath);
        if (!string.IsNullOrWhiteSpace(directory))
            PrivateAppleStoreFiles.EnsureDirectory(directory);
        string tempPath = $"{_options.StatePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            PrivateAppleStoreFiles.RestrictFile(tempPath);
            File.Move(tempPath, _options.StatePath, overwrite: true);
            PrivateAppleStoreFiles.RestrictFile(_options.StatePath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            throw new AppleAccountStateStoreException("The Apple account state could not be saved.", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed record AppleAccountState(
    int SchemaVersion,
    string AccountProfileId,
    string AppleIdHint,
    IReadOnlyList<AppleAccountTeamState> Teams,
    DateTimeOffset AuthValidatedAt,
    string? SelectedTeamId,
    DateTimeOffset? TeamValidatedAt,
    string LastAuthenticatedByActor = "legacy:unknown",
    string? TeamSelectedByActor = "legacy:unknown");

internal sealed record AppleAccountTeamState(string TeamId, string Name, string Type);

internal static class PrivateAppleStoreFiles
{
    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void HardenFiles(string directory)
    {
        EnsureDirectory(directory);
        foreach (string path in Directory.EnumerateFiles(directory))
            RestrictFile(path);
    }
}

internal sealed class AppleCredentialVersionConflictException : InvalidOperationException;
internal sealed class AppleCredentialSourceReadOnlyException : InvalidOperationException;
internal sealed class AppleAccountReplacementRequiresCutoverException : InvalidOperationException;
internal sealed class AppleAccountProfileNotFoundException : KeyNotFoundException;
internal sealed class AppleTeamSelectionStaleException : InvalidOperationException;
internal sealed class AppleTeamNotReturnedException : InvalidOperationException;

internal sealed class AppleCredentialStoreException(string message, Exception innerException)
    : Exception(message, innerException);

internal sealed class AppleAccountStateStoreException(string message, Exception innerException)
    : Exception(message, innerException);
