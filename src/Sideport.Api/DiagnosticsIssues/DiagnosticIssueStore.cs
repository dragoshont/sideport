using System.Text.Json;

namespace Sideport.Api.DiagnosticsIssues;

public sealed class DiagnosticIssueStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<DiagnosticIssueState>? _states;

    public DiagnosticIssueStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public async Task<IReadOnlyDictionary<string, DiagnosticIssueState>> ListStatesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _states!.ToDictionary(state => state.IssueId, StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DiagnosticIssueState> UpsertAsync(DiagnosticIssueState state, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            int index = _states!.FindIndex(item => string.Equals(item.IssueId, state.IssueId, StringComparison.Ordinal));
            DiagnosticIssueState? previous = index < 0 ? null : _states[index];
            if (index < 0)
                _states.Add(state);
            else
                _states[index] = state;

            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (previous is null)
                    _states.Remove(state);
                else
                    _states[index] = previous;
                throw;
            }
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_states is not null)
            return;
        if (!File.Exists(_path))
        {
            _states = [];
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(_path);
            _states = JsonSerializer.Deserialize<List<DiagnosticIssueState>>(stream, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new DiagnosticIssueStoreException("Diagnostic issue state could not be loaded.", ex);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, _states!.OrderBy(state => state.IssueId).ToArray(), JsonOptions, ct).ConfigureAwait(false);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DiagnosticIssueStoreException("Diagnostic issue state could not be saved.", ex);
        }
    }
}

public sealed record DiagnosticIssueState(string IssueId, string Status, string? Note, DateTimeOffset UpdatedAt);

public sealed class DiagnosticIssueStoreException(string message, Exception innerException) : Exception(message, innerException);
