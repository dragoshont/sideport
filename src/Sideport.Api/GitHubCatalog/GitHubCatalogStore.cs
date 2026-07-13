using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sideport.Api.GitHubCatalog;

internal sealed record GitHubStoredSource(
    string Id,
    string Repository,
    string Visibility,
    string Provider,
    long? RepositoryId,
    long? InstallationId,
    bool AllowPrereleases,
    bool Configured,
    DateTimeOffset CreatedAt);

internal sealed record GitHubStoredConnection(
    string Id,
    string Actor,
    string Repository,
    string Visibility,
    string Status,
    string IdempotencyKeyHash,
    string SemanticTarget,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    string? SourceId,
    string? Error);

internal sealed record GitHubStoredSetupState(
    string Hash,
    string ConnectionId,
    string Actor,
    string Repository,
    string Intent,
    string AllowedOrigin,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt);

internal sealed record GitHubCatalogStoreDocument(
    int SchemaVersion,
    IReadOnlyList<GitHubStoredSource> Sources,
    IReadOnlyList<GitHubStoredConnection> Connections,
    IReadOnlyList<GitHubStoredSetupState> SetupStates)
{
    public static GitHubCatalogStoreDocument Empty { get; } = new(1, [], [], []);
}

internal enum GitHubStateConsumeStatus
{
    Accepted,
    Invalid,
    Expired,
    Replayed,
}

internal sealed record GitHubStateConsumeResult(
    GitHubStateConsumeStatus Status,
    GitHubStoredSetupState? State,
    GitHubStoredConnection? Connection);

public sealed class GitHubCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GitHubCatalogStoreDocument? _document;

    public GitHubCatalogStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A GitHub catalog state path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    internal async Task EnsureConfiguredSourcesAsync(
        IReadOnlyList<GitHubStoredSource> configured,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            var sources = current.Sources.ToList();
            bool changed = false;
            foreach (GitHubStoredSource source in configured)
            {
                int index = sources.FindIndex(item => string.Equals(item.Id, source.Id, StringComparison.Ordinal));
                if (index < 0)
                {
                    sources.Add(source);
                    changed = true;
                    continue;
                }

                GitHubStoredSource existing = sources[index];
                if (!existing.Configured && !SameSourceIdentity(existing, source))
                    throw new InvalidOperationException($"Configured GitHub source ID '{source.Id}' conflicts with a dynamic source.");
                if (existing != source)
                {
                    sources[index] = source with { CreatedAt = existing.CreatedAt };
                    changed = true;
                }
            }

            if (changed)
                await SaveAndSetAsync(current with { Sources = sources }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<IReadOnlyList<GitHubStoredSource>> ListSourcesAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            return current.Sources.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<GitHubStoredSource?> FindSourceAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            return current.Sources.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<GitHubStoredSource> UpdateSourceIdentityAsync(
        string id,
        long repositoryId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            int index = current.Sources.ToList().FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (index < 0)
                throw new GitHubCatalogException("github-source-not-found", "The GitHub source was not found.");
            GitHubStoredSource existing = current.Sources[index];
            if (existing.RepositoryId is not null && existing.RepositoryId != repositoryId)
                throw new GitHubCatalogException("github-installation-invalid", "The configured GitHub repository identity changed.");
            if (existing.RepositoryId == repositoryId)
                return existing;

            var sources = current.Sources.ToList();
            GitHubStoredSource updated = existing with { RepositoryId = repositoryId };
            sources[index] = updated;
            await SaveAndSetAsync(current with { Sources = sources }, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<(GitHubStoredConnection Connection, bool Created)> ConnectPublicAsync(
        string actor,
        string keyHash,
        string semanticTarget,
        string repository,
        GitHubStoredSource proposedSource,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            GitHubStoredConnection? replay = FindIdempotent(current, actor, keyHash);
            if (replay is not null)
            {
                EnsureSameTarget(replay, semanticTarget);
                return (replay, false);
            }

            GitHubStoredSource source = current.Sources.FirstOrDefault(item =>
                string.Equals(item.Repository, repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Visibility, "public", StringComparison.Ordinal)) ?? proposedSource;
            var sources = current.Sources.ToList();
            if (!sources.Any(item => string.Equals(item.Id, source.Id, StringComparison.Ordinal)))
                sources.Add(source);

            var connection = new GitHubStoredConnection(
                NewId("ghcon_"), actor, repository, "public", "connected", keyHash,
                semanticTarget, now, now, null, source.Id, null);
            var connections = current.Connections.Append(connection).ToArray();
            await SaveAndSetAsync(current with { Sources = sources, Connections = connections }, ct).ConfigureAwait(false);
            return (connection, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<(GitHubStoredConnection Connection, bool Created)> StartPrivateConnectionAsync(
        string actor,
        string keyHash,
        string semanticTarget,
        string repository,
        GitHubStoredSetupState state,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            GitHubStoredConnection? replay = FindIdempotent(current, actor, keyHash);
            bool created = replay is null;
            GitHubStoredConnection connection;
            var connections = current.Connections.ToList();
            var states = current.SetupStates.ToList();
            if (replay is not null)
            {
                EnsureSameTarget(replay, semanticTarget);
                if (string.Equals(replay.Status, "connected", StringComparison.Ordinal))
                    return (replay, false);

                connection = replay with
                {
                    Status = "authorization-required",
                    UpdatedAt = now,
                    ExpiresAt = state.ExpiresAt,
                    Error = null,
                };
                int connectionIndex = connections.FindIndex(item => item.Id == connection.Id);
                connections[connectionIndex] = connection;
                for (int index = 0; index < states.Count; index++)
                {
                    if (states[index].ConnectionId == connection.Id && states[index].ConsumedAt is null)
                        states[index] = states[index] with { ConsumedAt = now };
                }
            }
            else
            {
                connection = new GitHubStoredConnection(
                    NewId("ghcon_"), actor, repository, "private", "authorization-required",
                    keyHash, semanticTarget, now, now, state.ExpiresAt, null, null);
                connections.Add(connection);
            }

            states.Add(state with { ConnectionId = connection.Id });
            await SaveAndSetAsync(current with { Connections = connections, SetupStates = states }, ct).ConfigureAwait(false);
            return (connection, created);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<GitHubStoredConnection?> FindConnectionAsync(
        string id,
        string actor,
        bool owner,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            return current.Connections.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal) &&
                (owner || string.Equals(item.Actor, actor, StringComparison.Ordinal)));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<GitHubStateConsumeResult> ConsumeStateAsync(
        string hash,
        string intent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            int index = FindStateIndex(current.SetupStates, hash);
            if (index < 0)
                return new(GitHubStateConsumeStatus.Invalid, null, null);

            GitHubStoredSetupState state = current.SetupStates[index];
            GitHubStoredConnection? connection = current.Connections.FirstOrDefault(item => item.Id == state.ConnectionId);
            if (state.ConsumedAt is not null)
                return new(GitHubStateConsumeStatus.Replayed, state, connection);
            if (state.ExpiresAt <= now)
            {
                if (connection is not null)
                {
                    var expiredConnections = current.Connections.Select(item => item.Id == connection.Id
                        ? item with { Status = "expired", UpdatedAt = now, Error = "github-state-expired" }
                        : item).ToArray();
                    await SaveAndSetAsync(current with { Connections = expiredConnections }, ct).ConfigureAwait(false);
                }
                return new(GitHubStateConsumeStatus.Expired, state, connection);
            }

            var states = current.SetupStates.ToList();
            state = state with { ConsumedAt = now };
            states[index] = state;
            await SaveAndSetAsync(current with { SetupStates = states }, ct).ConfigureAwait(false);

            bool bindingsMatch = connection is not null &&
                string.Equals(connection.Actor, state.Actor, StringComparison.Ordinal) &&
                string.Equals(connection.Repository, state.Repository, StringComparison.Ordinal) &&
                string.Equals(state.Intent, intent, StringComparison.Ordinal);
            return bindingsMatch
                ? new(GitHubStateConsumeStatus.Accepted, state, connection)
                : new(GitHubStateConsumeStatus.Invalid, state, connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<GitHubStoredConnection> CompleteConnectionAsync(
        string connectionId,
        GitHubStoredSource source,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            int index = current.Connections.ToList().FindIndex(item => item.Id == connectionId);
            if (index < 0)
                throw new GitHubCatalogException("github-state-invalid", "The GitHub setup state is invalid.");
            var connections = current.Connections.ToList();
            GitHubStoredConnection updated = connections[index] with
            {
                Status = "connected",
                UpdatedAt = now,
                ExpiresAt = null,
                SourceId = source.Id,
                Error = null,
            };
            connections[index] = updated;
            var sources = current.Sources.ToList();
            int sourceIndex = sources.FindIndex(item => item.Id == source.Id);
            if (sourceIndex < 0)
                sources.Add(source);
            else
                sources[sourceIndex] = source;
            await SaveAndSetAsync(current with { Connections = connections, Sources = sources }, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task FailConnectionAsync(
        string connectionId,
        string safeErrorCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GitHubCatalogStoreDocument current = await LoadAsync(ct).ConfigureAwait(false);
            var connections = current.Connections.Select(item => item.Id == connectionId
                ? item with { Status = "failed", UpdatedAt = now, ExpiresAt = null, Error = safeErrorCode }
                : item).ToArray();
            await SaveAndSetAsync(current with { Connections = connections }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string HashOpaque(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task<GitHubCatalogStoreDocument> LoadAsync(CancellationToken ct)
    {
        if (_document is not null)
            return _document;
        if (!File.Exists(_path))
            return _document = GitHubCatalogStoreDocument.Empty;

        try
        {
            await using FileStream stream = File.OpenRead(_path);
            GitHubCatalogStoreDocument? loaded = await JsonSerializer.DeserializeAsync<GitHubCatalogStoreDocument>(
                stream, JsonOptions, ct).ConfigureAwait(false);
            return _document = loaded is { SchemaVersion: 1 } ? loaded : GitHubCatalogStoreDocument.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new GitHubCatalogException("github-store-unavailable", "GitHub source state could not be loaded.");
        }
    }

    private async Task SaveAndSetAsync(GitHubCatalogStoreDocument document, CancellationToken ct)
    {
        string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            await using (FileStream stream = new(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(tempPath, _path, overwrite: true);
            _document = document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new GitHubCatalogException("github-store-unavailable", "GitHub source state could not be saved.");
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static GitHubStoredConnection? FindIdempotent(
        GitHubCatalogStoreDocument document,
        string actor,
        string keyHash) => document.Connections.FirstOrDefault(item =>
            string.Equals(item.Actor, actor, StringComparison.Ordinal) &&
            string.Equals(item.IdempotencyKeyHash, keyHash, StringComparison.Ordinal));

    private static void EnsureSameTarget(GitHubStoredConnection connection, string semanticTarget)
    {
        if (!string.Equals(connection.SemanticTarget, semanticTarget, StringComparison.Ordinal))
            throw new GitHubCatalogException("idempotency-target-conflict", "This idempotency key was used for another GitHub source.");
    }

    private static int FindStateIndex(IReadOnlyList<GitHubStoredSetupState> states, string hash)
    {
        byte[] candidate;
        try
        {
            candidate = Convert.FromHexString(hash);
        }
        catch (FormatException)
        {
            return -1;
        }

        for (int index = 0; index < states.Count; index++)
        {
            byte[] stored;
            try
            {
                stored = Convert.FromHexString(states[index].Hash);
            }
            catch (FormatException)
            {
                continue;
            }
            if (stored.Length == candidate.Length && CryptographicOperations.FixedTimeEquals(stored, candidate))
                return index;
        }
        return -1;
    }

    private static bool SameSourceIdentity(GitHubStoredSource left, GitHubStoredSource right) =>
        string.Equals(left.Repository, right.Repository, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Visibility, right.Visibility, StringComparison.Ordinal);

    private static string NewId(string prefix) =>
        prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
