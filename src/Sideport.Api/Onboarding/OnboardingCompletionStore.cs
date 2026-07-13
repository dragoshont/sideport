using System.Text.Json;
using Sideport.Api.Operations;

namespace Sideport.Api.Onboarding;

public sealed record OnboardingCompletionReceipt(
    int SchemaVersion,
    DateTimeOffset CompletedAt,
    OperationActorDto Actor,
    string AccountProfileId,
    string TeamId,
    string DeviceUdid,
    string CatalogAppId,
    int CatalogVersion,
    string CatalogSha256,
    string BundleId,
    string VerifiedOperationId,
    string SchedulerSettingsVersion,
    DateTimeOffset OperationalCheckedAt);

/// <summary>
/// Stores the one immutable receipt that proves first-run setup completed.
/// Current health is deliberately kept elsewhere: an offline phone or a later
/// Apple outage must not erase historical completion evidence.
/// </summary>
public sealed class OnboardingCompletionStore
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OnboardingCompletionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<OnboardingCompletionReceipt?> ReadAsync(CancellationToken ct = default)
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

    public async Task<(OnboardingCompletionReceipt Receipt, bool Created)> CreateAsync(
        OnboardingCompletionReceipt receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(receipt);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OnboardingCompletionReceipt? existing = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (existing is not null)
                return (existing, false);

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, receipt, JsonOptions, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }

                File.Move(temporaryPath, _path);
                return (receipt, true);
            }
            catch (OperationCanceledException)
            {
                TryDelete(temporaryPath);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporaryPath);
                throw new OnboardingCompletionStoreException(
                    "The onboarding completion receipt could not be written.",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OnboardingCompletionReceipt?> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            await using FileStream stream = File.OpenRead(_path);
            OnboardingCompletionReceipt? receipt = await JsonSerializer.DeserializeAsync<OnboardingCompletionReceipt>(
                stream,
                JsonOptions,
                ct).ConfigureAwait(false);
            if (receipt is null)
                throw new InvalidDataException("The onboarding completion receipt is empty.");
            Validate(receipt);
            return receipt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OnboardingCompletionStoreException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw new OnboardingCompletionStoreException(
                "The onboarding completion receipt is unavailable.",
                ex);
        }
    }

    private static void Validate(OnboardingCompletionReceipt receipt)
    {
        if (receipt.SchemaVersion != CurrentSchemaVersion ||
            receipt.CompletedAt == default ||
            receipt.Actor is null ||
            string.IsNullOrWhiteSpace(receipt.Actor.Kind) ||
            string.IsNullOrWhiteSpace(receipt.Actor.DisplayName) ||
            string.IsNullOrWhiteSpace(receipt.AccountProfileId) ||
            string.IsNullOrWhiteSpace(receipt.TeamId) ||
            string.IsNullOrWhiteSpace(receipt.DeviceUdid) ||
            string.IsNullOrWhiteSpace(receipt.CatalogAppId) ||
            receipt.CatalogVersion < 1 ||
            string.IsNullOrWhiteSpace(receipt.CatalogSha256) ||
            string.IsNullOrWhiteSpace(receipt.BundleId) ||
            string.IsNullOrWhiteSpace(receipt.VerifiedOperationId) ||
            string.IsNullOrWhiteSpace(receipt.SchedulerSettingsVersion) ||
            receipt.OperationalCheckedAt == default)
        {
            throw new InvalidDataException("The onboarding completion receipt is invalid.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class OnboardingCompletionStoreException(string message, Exception innerException)
    : Exception(message, innerException);
