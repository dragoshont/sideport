using System.IO.Compression;
using Claunia.PropertyList;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi.Packaging;

/// <summary>
/// Identifying + lifecycle metadata read from an IPA's app bundle.
/// </summary>
public sealed record IpaInfo(
    string BundleIdentifier,
    string? DisplayName,
    string? Version,
    string? ShortVersion,
    string ExecutableName,
    string AppBundleName,
    ProvisioningProfileInfo? Profile)
{
    /// <summary>The signing expiry from the embedded profile, if any.</summary>
    public DateTimeOffset? SignatureExpiresAt => Profile?.ExpirationDate;
}

/// <summary>
/// Resource limits applied while inspecting an IPA. The defaults are intentionally
/// generous for real applications while still bounding archive metadata and the
/// only entries Sideport decompresses into memory.
/// </summary>
public sealed record IpaInspectionLimits
{
    public static IpaInspectionLimits Default { get; } = new();

    public int MaxEntryCount { get; init; } = 65_536;

    public long MaxTargetEntryBytes { get; init; } = 16 * 1024 * 1024;

    public long MaxTotalUncompressedBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 200d;

    internal void Validate()
    {
        if (MaxEntryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxEntryCount));
        if (MaxTargetEntryBytes <= 0 || MaxTargetEntryBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaxTargetEntryBytes));
        if (MaxTotalUncompressedBytes <= 0 || MaxTotalUncompressedBytes < MaxTargetEntryBytes)
            throw new ArgumentOutOfRangeException(nameof(MaxTotalUncompressedBytes));
        if (!double.IsFinite(MaxCompressionRatio) || MaxCompressionRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
    }
}

/// <summary>
/// Reads identifying + signing metadata from an IPA (a zip whose layout is
/// <c>Payload/&lt;App&gt;.app/{Info.plist, embedded.mobileprovision, …}</c>),
/// without extracting or executing anything. <c>Info.plist</c> may be binary or
/// XML; <c>plist-cil</c> handles both. The embedded profile, when present, is
/// parsed via <see cref="MobileProvision"/>.
/// </summary>
public static class IpaInspector
{
    private const long MaxIconBytes = 4 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    /// <summary>Inspect an IPA on disk.</summary>
    public static IpaInfo Inspect(string ipaPath) => Inspect(ipaPath, IpaInspectionLimits.Default);

    /// <summary>Inspect an IPA on disk with explicit archive resource limits.</summary>
    public static IpaInfo Inspect(string ipaPath, IpaInspectionLimits limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipaPath);
        ArgumentNullException.ThrowIfNull(limits);
        using FileStream stream = File.OpenRead(ipaPath);
        return Inspect(stream, limits);
    }

    /// <summary>Inspect an IPA from a (seekable) stream.</summary>
    public static IpaInfo Inspect(Stream ipaStream) => Inspect(ipaStream, IpaInspectionLimits.Default);

    /// <summary>Inspect an IPA from a (seekable) stream with explicit archive resource limits.</summary>
    public static IpaInfo Inspect(Stream ipaStream, IpaInspectionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(ipaStream);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        using var archive = new ZipArchive(ipaStream, ZipArchiveMode.Read, leaveOpen: true);
        IReadOnlyList<ValidatedArchiveEntry> entries = ValidateArchiveDirectory(archive, limits);

        string appPrefix = FindAppBundlePrefix(entries)
            ?? throw new FormatException("not a valid IPA: no Payload/*.app found");

        ZipArchiveEntry infoEntry = FindUniqueTarget(entries, appPrefix + "Info.plist")
            ?? throw new FormatException($"IPA missing {appPrefix}Info.plist");

        NSDictionary info = PlistCodec.ParseDictionary(ReadTargetEntry(infoEntry, "Info.plist", limits));

        string bundleId = PlistCodec.GetStringOrNull(info, "CFBundleIdentifier")
            ?? throw new FormatException("Info.plist missing CFBundleIdentifier");
        string executable = PlistCodec.GetStringOrNull(info, "CFBundleExecutable")
            ?? throw new FormatException("Info.plist missing CFBundleExecutable");
        string? displayName = PlistCodec.GetStringOrNull(info, "CFBundleDisplayName")
            ?? PlistCodec.GetStringOrNull(info, "CFBundleName");
        string? version = PlistCodec.GetStringOrNull(info, "CFBundleVersion");
        string? shortVersion = PlistCodec.GetStringOrNull(info, "CFBundleShortVersionString");

        string appBundleName = appPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        ProvisioningProfileInfo? profile = null;
        ZipArchiveEntry? profileEntry = FindUniqueTarget(entries, appPrefix + "embedded.mobileprovision");
        if (profileEntry is not null)
            profile = MobileProvision.Parse(ReadTargetEntry(profileEntry, "embedded.mobileprovision", limits));

        return new IpaInfo(bundleId, displayName, version, shortVersion, executable, appBundleName, profile);
    }

    /// <summary>Extract one bounded PNG app icon from the top-level app bundle.</summary>
    public static byte[]? ExtractIconPng(string ipaPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipaPath);
        using FileStream stream = File.OpenRead(ipaPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        IReadOnlyList<ValidatedArchiveEntry> entries = ValidateArchiveDirectory(archive, IpaInspectionLimits.Default);
        string appPrefix = FindAppBundlePrefix(entries) ?? throw new FormatException("not a valid IPA: no Payload/*.app found");
        ValidatedArchiveEntry[] candidates = entries
            .Where(entry => entry.Name.StartsWith(appPrefix, StringComparison.Ordinal) &&
                            entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                            Path.GetFileName(entry.Name).Contains("Icon", StringComparison.OrdinalIgnoreCase) &&
                            entry.Entry.Length > 0)
            .OrderByDescending(entry => entry.Entry.Length)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (ValidatedArchiveEntry candidate in candidates)
        {
            if (candidate.Entry.Length > MaxIconBytes)
                throw new InvalidDataException($"IPA app icon exceeds the {MaxIconBytes} byte limit.");
            byte[] bytes = ReadTargetEntry(candidate.Entry, "app icon", IpaInspectionLimits.Default with { MaxTargetEntryBytes = MaxIconBytes });
            if (IsBoundedPng(bytes)) return bytes;
        }
        return null;
    }

    private static bool IsBoundedPng(byte[] bytes)
    {
        if (bytes.Length < 24 || !bytes.AsSpan().StartsWith(PngSignature) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= 2048 && height is > 0 and <= 2048;
    }

    /// <summary>
    /// The <c>Payload/&lt;App&gt;.app/</c> prefix (with trailing slash) of the
    /// single top-level app bundle, or <see langword="null"/> if none.
    /// </summary>
    private static string? FindAppBundlePrefix(IReadOnlyList<ValidatedArchiveEntry> entries)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (ValidatedArchiveEntry entry in entries)
        {
            string name = entry.Name;
            if (!name.StartsWith("Payload/", StringComparison.Ordinal))
                continue;

            string remainder = name["Payload/".Length..];
            int separator = remainder.IndexOf('/');
            if (separator < 0)
                continue;

            string appDirectory = remainder[..separator];
            if (appDirectory.Length > ".app".Length && appDirectory.EndsWith(".app", StringComparison.Ordinal))
                prefixes.Add($"Payload/{appDirectory}/");
        }

        return prefixes.Count switch
        {
            0 => null,
            1 => prefixes.Single(),
            _ => throw new FormatException("not a valid IPA: multiple top-level Payload/*.app bundles found"),
        };
    }

    private static IReadOnlyList<ValidatedArchiveEntry> ValidateArchiveDirectory(
        ZipArchive archive,
        IpaInspectionLimits limits)
    {
        if (archive.Entries.Count > limits.MaxEntryCount)
            throw new InvalidDataException($"IPA contains more than {limits.MaxEntryCount} ZIP entries.");

        var entries = new List<ValidatedArchiveEntry>(archive.Entries.Count);
        long totalUncompressed = 0;
        long totalCompressed = 0;
        try
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = NormalizeAndValidateEntryName(entry.FullName);
                long uncompressed = entry.Length;
                long compressed = entry.CompressedLength;
                if (uncompressed < 0 || compressed < 0)
                    throw new InvalidDataException("IPA contains an entry with an invalid ZIP size.");
                if (ExceedsCompressionRatio(uncompressed, compressed, limits.MaxCompressionRatio))
                    throw new InvalidDataException($"IPA ZIP entry '{name}' exceeds the allowed compression ratio.");

                totalUncompressed = checked(totalUncompressed + uncompressed);
                totalCompressed = checked(totalCompressed + compressed);
                if (totalUncompressed > limits.MaxTotalUncompressedBytes)
                    throw new InvalidDataException($"IPA expands beyond the {limits.MaxTotalUncompressedBytes} byte limit.");

                entries.Add(new ValidatedArchiveEntry(entry, name));
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("IPA ZIP size metadata overflowed the inspection limit.", ex);
        }

        if (ExceedsCompressionRatio(totalUncompressed, totalCompressed, limits.MaxCompressionRatio))
            throw new InvalidDataException("IPA archive exceeds the allowed aggregate compression ratio.");

        return entries;
    }

    private static string NormalizeAndValidateEntryName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName) || fullName.Contains('\0'))
            throw new FormatException("IPA contains an invalid empty ZIP entry path.");

        string name = fullName.Replace('\\', '/');
        if (name.StartsWith("/", StringComparison.Ordinal) ||
            (name.Length >= 2 && char.IsLetter(name[0]) && name[1] == ':'))
        {
            throw new FormatException("IPA contains a rooted ZIP entry path.");
        }

        string path = name.EndsWith("/", StringComparison.Ordinal) ? name[..^1] : name;
        if (path.Length == 0)
            throw new FormatException("IPA contains an invalid empty ZIP entry path.");

        string[] segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new FormatException("IPA contains a traversing or ambiguous ZIP entry path.");

        return name;
    }

    private static ZipArchiveEntry? FindUniqueTarget(
        IReadOnlyList<ValidatedArchiveEntry> entries,
        string targetName)
    {
        ValidatedArchiveEntry[] matches = entries
            .Where(entry => string.Equals(entry.Name, targetName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1)
            throw new FormatException($"IPA contains duplicate {targetName} entries.");
        return matches.Length == 0 ? null : matches[0].Entry;
    }

    private static byte[] ReadTargetEntry(
        ZipArchiveEntry entry,
        string label,
        IpaInspectionLimits limits)
    {
        if (entry.Length > limits.MaxTargetEntryBytes)
            throw new InvalidDataException($"IPA {label} exceeds the {limits.MaxTargetEntryBytes} byte inspection limit.");

        using Stream source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        byte[] buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            total += read;
            if (total > limits.MaxTargetEntryBytes)
                throw new InvalidDataException($"IPA {label} exceeds the {limits.MaxTargetEntryBytes} byte inspection limit.");
            destination.Write(buffer, 0, read);
        }

        if (total != entry.Length)
            throw new InvalidDataException($"IPA {label} decompressed size does not match its ZIP metadata.");
        return destination.ToArray();
    }

    private static bool ExceedsCompressionRatio(long uncompressed, long compressed, double maximum) =>
        uncompressed > 0 && (compressed == 0 || (double)uncompressed / compressed > maximum);

    private sealed record ValidatedArchiveEntry(ZipArchiveEntry Entry, string Name);
}
