using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sideport.Api.Catalog;

namespace Sideport.Api.Tests;

public sealed class CatalogV2ServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sideport-catalog-v2-{Guid.NewGuid():N}");

    public CatalogV2ServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task RootImport_UsesManagedCopy_AndPublicDtosArePathFree()
    {
        string root = Path.Combine(_directory, "root");
        string source = WriteIpa(root, "Sample.ipa", "com.example.sample", "1");
        FileAppCatalog catalog = CreateCatalog("managed", roots: [new("shared", "Shared apps", root)]);

        CatalogV2MutationResult imported = await catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "Sample.ipa", Id: "sample"),
            "user:one");
        CatalogAppDto internalEntry = Assert.Single(await catalog.ListAsync());
        IReadOnlyList<CatalogImportRootDto> roots = await catalog.ListImportRootsAsync();

        Assert.True(imported.Created);
        Assert.NotEqual(Path.GetFullPath(source), internalEntry.IpaPath);
        Assert.True(File.Exists(internalEntry.IpaPath));
        File.Delete(source);
        Assert.True(File.Exists(internalEntry.IpaPath));
        string appJson = JsonSerializer.Serialize(imported.Entry, WebJson);
        string rootsJson = JsonSerializer.Serialize(roots, WebJson);
        Assert.False(appJson.Contains("ipaPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(appJson.Contains(_directory, StringComparison.Ordinal));
        Assert.False(rootsJson.Contains("path", StringComparison.OrdinalIgnoreCase));
        Assert.False(rootsJson.Contains(_directory, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RootImport_RejectsTraversalWrongCaseMissingAndNonIpa()
    {
        string root = Path.Combine(_directory, "root");
        WriteIpa(root, "Good.ipa", "com.example.good", "1");
        await File.WriteAllTextAsync(Path.Combine(root, "Notes.txt"), "not an ipa");
        FileAppCatalog catalog = CreateCatalog("paths", roots: [new("shared", "Shared apps", root)]);

        await AssertCode("catalog-path-invalid", () => catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "../Good.ipa"), "user:one"));
        await AssertCode("catalog-path-invalid", () => catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "good.ipa"), "user:one"));
        await AssertCode("catalog-source-not-found", () => catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "Missing.ipa"), "user:one"));
        await AssertCode("catalog-path-invalid", () => catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "Notes.txt"), "user:one"));
    }

    [Fact]
    public async Task RootImport_RejectsSymlinkEscapeEvenWhenLaterSegmentReturnsInsideRoot()
    {
        string root = Path.Combine(_directory, "root");
        string outside = Path.Combine(_directory, "outside");
        WriteIpa(root, "Good.ipa", "com.example.good", "1");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "Escape"), outside);
            Directory.CreateSymbolicLink(Path.Combine(outside, "Back"), root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        FileAppCatalog catalog = CreateCatalog("links", roots: [new("shared", "Shared apps", root)]);
        await AssertCode("catalog-path-outside-root", () => catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "Escape/Back/Good.ipa"), "user:one"));
    }

    [Fact]
    public async Task UploadLimits_RejectAndCleanStaging()
    {
        string ipa = WriteIpa(Path.Combine(_directory, "sources"), "Sample.ipa", "com.example.sample", "1");
        FileAppCatalog sizeCatalog = CreateCatalog("size", maxBytes: 8);
        await AssertCode("upload-too-large", () => sizeCatalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(ipa), "user:one"));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "size", "imports", ".staging")));

        string crowded = WriteCrowdedArchive(Path.Combine(_directory, "sources"), "Crowded.ipa");
        FileAppCatalog archiveCatalog = CreateCatalog("archive", maxBytes: 64 * 1024 * 1024);
        await AssertCode("ipa-inspection-failed", () => archiveCatalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(crowded), "user:one"));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "archive", "imports", ".staging")));
    }

    [Fact]
    public async Task CasAndIdempotency_AreVersionBound()
    {
        string firstIpa = WriteIpa(Path.Combine(_directory, "first"), "Sample.ipa", "com.example.sample", "1");
        string secondIpa = WriteIpa(Path.Combine(_directory, "second"), "Sample.ipa", "com.example.sample", "2");
        FileAppCatalog catalog = CreateCatalog("versions");
        var firstRequest = new CatalogUploadV2Request(firstIpa, Id: "sample", IdempotencyKey: "request-1");

        CatalogV2MutationResult first = await catalog.ImportUploadedIpaV2Async(firstRequest, "user:one");
        CatalogV2MutationResult replay = await catalog.ImportUploadedIpaV2Async(firstRequest, "user:one");
        Assert.Equal(1, first.Entry.CatalogVersion);
        Assert.True(replay.Replayed);
        await AssertCode("idempotency-target-conflict", () => catalog.ImportUploadedIpaV2Async(
            firstRequest with { Purpose = "Different intent" }, "user:one"));
        await AssertCode("catalog-version-conflict", () => catalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(secondIpa, Id: "sample"), "user:one"));

        CatalogV2MutationResult replaced = await catalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(secondIpa, Id: "sample", ExpectedCatalogVersion: 1), "user:one");
        Assert.Equal(2, replaced.Entry.CatalogVersion);
        await AssertCode("idempotency-target-conflict", () => catalog.ImportUploadedIpaV2Async(firstRequest, "user:one"));
        Assert.Single(FilesUnder(Path.Combine(_directory, "versions", "imports", ".managed"), "*.ipa"));
    }

    [Fact]
    public async Task SameBytesFromAnotherSource_MergeProvenanceWithoutRepublishing()
    {
        string source = WriteIpa(Path.Combine(_directory, "source"), "Sample.ipa", "com.example.sample", "1");
        string root = Path.Combine(_directory, "root");
        Directory.CreateDirectory(root);
        File.Copy(source, Path.Combine(root, "Sample.ipa"));
        FileAppCatalog catalog = CreateCatalog("provenance", roots: [new("shared", "Shared apps", root)]);

        await catalog.ImportUploadedIpaV2Async(new CatalogUploadV2Request(source, Id: "sample"), "user:one");
        CatalogV2MutationResult merged = await catalog.ImportFromRootV2Async(
            new CatalogRootImportRequest("shared", "Sample.ipa", Id: "sample", ExpectedCatalogVersion: 1),
            "user:one");

        Assert.Equal(2, merged.Entry.CatalogVersion);
        Assert.Equal(2, merged.Entry.ArtifactSources.Count);
        Assert.Single(FilesUnder(Path.Combine(_directory, "provenance", "imports", ".managed"), "*.ipa"));
    }

    [Fact]
    public async Task FailedCatalogSave_RollsBackEntryAndPublishedArtifact()
    {
        string firstIpa = WriteIpa(Path.Combine(_directory, "first"), "Sample.ipa", "com.example.sample", "1");
        string secondIpa = WriteIpa(Path.Combine(_directory, "second"), "Sample.ipa", "com.example.sample", "2");
        FileAppCatalog catalog = CreateCatalog("rollback");
        await catalog.ImportUploadedIpaV2Async(new CatalogUploadV2Request(firstIpa, Id: "sample"), "user:one");
        CatalogAppDto original = Assert.Single(await catalog.ListAsync());
        byte[] originalBytes = await File.ReadAllBytesAsync(original.IpaPath);
        string catalogPath = Path.Combine(_directory, "rollback", "catalog.json");
        File.Delete(catalogPath);
        Directory.CreateDirectory(catalogPath);

        await Assert.ThrowsAsync<CatalogStoreException>(() => catalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(secondIpa, Id: "sample", ExpectedCatalogVersion: 1), "user:one"));

        CatalogAppDto restored = Assert.Single(await catalog.ListAsync());
        Assert.Equal(1, restored.CatalogVersion);
        Assert.Equal(original.IpaPath, restored.IpaPath);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(restored.IpaPath));
        Assert.Single(FilesUnder(Path.Combine(_directory, "rollback", "imports", ".managed"), "*.ipa"));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "rollback"), "*.publishing"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "rollback"), "catalog.json.*.tmp"));
    }

    [Fact]
    public async Task LegacyPathBearingArray_LoadsWithoutRewriteOnV2Read()
    {
        string state = Path.Combine(_directory, "legacy");
        Directory.CreateDirectory(state);
        string catalogPath = Path.Combine(state, "catalog.json");
        string legacyPath = Path.Combine(state, "old.ipa");
        var legacy = new CatalogAppDto(
            "legacy", "Legacy", "Existing app", "com.example.legacy", legacyPath,
            "1", "1.0", 123, "abc", false, null, "upload", "ready",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"), []);
        string before = JsonSerializer.Serialize(new[] { legacy }, WebJson);
        await File.WriteAllTextAsync(catalogPath, before);
        FileAppCatalog catalog = CreateCatalog("legacy");

        CatalogAppV2Dto entry = Assert.Single(await catalog.ListV2Async());

        Assert.Equal("legacy", entry.Id);
        Assert.Equal(before, await File.ReadAllTextAsync(catalogPath));
        string json = JsonSerializer.Serialize(entry, WebJson);
        Assert.False(json.Contains("ipaPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains(legacyPath, StringComparison.Ordinal));
    }

    private FileAppCatalog CreateCatalog(
        string name,
        long maxBytes = 256 * 1024 * 1024,
        IReadOnlyList<AppCatalogImportRoot>? roots = null) =>
        new(new AppCatalogOptions(
            Path.Combine(_directory, name, "catalog.json"),
            Path.Combine(_directory, name, "imports"),
            maxBytes,
            [],
            roots));

    private static async Task AssertCode(string expected, Func<Task> action)
    {
        CatalogV2Exception error = await Assert.ThrowsAsync<CatalogV2Exception>(action);
        Assert.Equal(expected, error.Code);
    }

    private static string WriteIpa(string directory, string filename, string bundleId, string version)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, filename);
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("Payload/Test.app/Info.plist");
        using Stream target = entry.Open();
        byte[] plist = Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>{{bundleId}}</string>
              <key>CFBundleDisplayName</key><string>Sample</string>
              <key>CFBundleExecutable</key><string>Sample</string>
              <key>CFBundleVersion</key><string>{{version}}</string>
              <key>CFBundleShortVersionString</key><string>{{version}}.0</string>
            </dict></plist>
            """);
        target.Write(plist);
        return path;
    }

    private static string WriteCrowdedArchive(string directory, string filename)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, filename);
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        for (int index = 0; index <= 10_000; index++)
            archive.CreateEntry($"Payload/Junk-{index:D5}");
        return path;
    }

    private static string[] FilesUnder(string directory, string pattern = "*") =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, pattern, SearchOption.AllDirectories) : [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
