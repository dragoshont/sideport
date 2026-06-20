namespace Sideport.Orchestrator;

/// <summary>
/// Durable, PVC-backed store for the INPUT IPAs that registrations point at.
/// <see cref="FileAppRegistry"/> persists the registration JSON, but the IPA
/// artifact itself must survive restarts too — otherwise the scheduler's
/// unattended refresh fails with "input IPA not found" once the pod's ephemeral
/// <c>/tmp</c> is wiped. One file per app (<c>udid/bundleId.ipa</c>), atomic
/// replace — the same boring shape as <see cref="FileAppRegistry"/>.
/// </summary>
public sealed class IpaStore
{
    private readonly string _root;

    public IpaStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
    }

    /// <summary>The durable path an app's IPA is (or would be) stored at.</summary>
    public string PathFor(string udid, string bundleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        string path = Path.GetFullPath(Path.Combine(_root, Sanitize(udid), $"{Sanitize(bundleId)}.ipa"));
        // Defence in depth: never let crafted identifiers escape the store root.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("app identifiers resolve outside the IPA store");
        return path;
    }

    /// <summary>
    /// Copy <paramref name="sourceIpaPath"/> into durable storage and return the
    /// durable path. If the source already IS the durable copy (a re-registration
    /// of an already-stored app), it is left in place. Atomic (temp file + move).
    /// </summary>
    public async Task<string> StoreAsync(string udid, string bundleId, string sourceIpaPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceIpaPath);

        string destination = PathFor(udid, bundleId);
        if (string.Equals(Path.GetFullPath(sourceIpaPath), destination, StringComparison.Ordinal))
            return destination;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = $"{destination}.{Guid.NewGuid():N}.tmp";
        await using (FileStream source = File.OpenRead(sourceIpaPath))
        await using (FileStream sink = File.Create(temp))
        {
            await source.CopyToAsync(sink, ct).ConfigureAwait(false);
        }
        File.Move(temp, destination, overwrite: true);
        return destination;
    }

    /// <summary>Delete an app's stored IPA (best effort), tidying an empty device dir.</summary>
    public void Remove(string udid, string bundleId)
    {
        string path = PathFor(udid, bundleId);
        if (File.Exists(path))
            File.Delete(path);

        string? dir = Path.GetDirectoryName(path);
        if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }

    // Map identifier characters that are illegal in a path segment to '_'. The
    // PathFor root check above is the real traversal guard; this just keeps the
    // on-disk names tidy and valid.
    private static string Sanitize(string value) =>
        string.Join("_", value.Split(Path.GetInvalidFileNameChars()));
}
