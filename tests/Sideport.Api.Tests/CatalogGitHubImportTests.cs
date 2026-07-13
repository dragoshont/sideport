using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.Api.Catalog;

namespace Sideport.Api.Tests;

public sealed class CatalogGitHubImportTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sideport-catalog-github-{Guid.NewGuid():N}");

    public CatalogGitHubImportTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Import_UsesManagedCopyAndReturnsPathFreeGitHubProvenance()
    {
        string source = WriteIpa("source", "Sample.ipa", "com.example.sample", "1");
        FileAppCatalog catalog = CreateCatalog("success");
        CatalogGitHubImportRequest request = Request(source, id: "sample", idempotencyKey: "github-1");

        CatalogV2MutationResult imported = await catalog.ImportDownloadedGitHubIpaV2Async(request, "user:one");
        CatalogAppDto internalEntry = Assert.Single(await catalog.ListAsync());

        Assert.True(imported.Created);
        Assert.False(imported.Replayed);
        Assert.NotEqual(Path.GetFullPath(source), internalEntry.IpaPath);
        Assert.True(File.Exists(internalEntry.IpaPath));
        File.Delete(source);
        Assert.True(File.Exists(internalEntry.IpaPath));

        CatalogArtifactSourceDto provenance = Assert.Single(imported.Entry.ArtifactSources);
        Assert.Equal("github-release", provenance.Kind);
        Assert.Equal("GitHub release", provenance.Label);
        Assert.Equal("dragoshont/sideport", provenance.Repository);
        Assert.Equal("v1.0", provenance.ReleaseTag);
        Assert.Equal("Sample.ipa", provenance.AssetName);

        string json = JsonSerializer.Serialize(imported.Entry, WebJson);
        Assert.DoesNotContain("ipaPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, json, StringComparison.Ordinal);

        string existingSourceJson = JsonSerializer.Serialize(
            new CatalogArtifactSourceDto("browser-upload", "This computer"),
            WebJson);
        Assert.Equal("{\"kind\":\"browser-upload\",\"label\":\"This computer\"}", existingSourceJson);
    }

    [Fact]
    public async Task Import_PreservesIdempotencyImmutableReplayAndCas()
    {
        string first = WriteIpa("first", "Sample.ipa", "com.example.sample", "1");
        string second = WriteIpa("second", "Sample.ipa", "com.example.sample", "2");
        FileAppCatalog catalog = CreateCatalog("semantics");
        CatalogGitHubImportRequest initial = Request(first, id: "sample", idempotencyKey: "github-1");

        CatalogV2MutationResult created = await catalog.ImportDownloadedGitHubIpaV2Async(initial, "user:one");
        CatalogV2MutationResult keyReplay = await catalog.ImportDownloadedGitHubIpaV2Async(initial, "user:one");
        CatalogV2MutationResult sourceReplay = await catalog.ImportDownloadedGitHubIpaV2Async(
            initial with { IdempotencyKey = null },
            "user:one");

        Assert.True(created.Created);
        Assert.True(keyReplay.Replayed);
        Assert.True(sourceReplay.Replayed);
        await AssertCode("idempotency-target-conflict", () => catalog.ImportDownloadedGitHubIpaV2Async(
            initial with
            {
                AssetId = 201,
                ImmutableSourceFingerprint = initial.ImmutableSourceFingerprint.Replace(":200:", ":201:", StringComparison.Ordinal),
            },
            "user:one"));

        CatalogGitHubImportRequest changed = Request(
            second,
            id: "sample",
            assetId: 201,
            releaseTag: "v2.0",
            expectedCatalogVersion: null);
        await AssertCode("catalog-version-conflict", () =>
            catalog.ImportDownloadedGitHubIpaV2Async(changed, "user:one"));

        CatalogV2MutationResult replaced = await catalog.ImportDownloadedGitHubIpaV2Async(
            changed with { ExpectedCatalogVersion = 1 },
            "user:one");
        Assert.Equal(2, replaced.Entry.CatalogVersion);
        Assert.Single(FilesUnder(Path.Combine(_directory, "semantics", "imports", ".managed"), "*.ipa"));
    }

    [Fact]
    public async Task Import_RejectsDigestMismatchWithoutPublishing()
    {
        string source = WriteIpa("digest", "Sample.ipa", "com.example.sample", "1");
        FileAppCatalog catalog = CreateCatalog("digest-mismatch");
        CatalogGitHubImportRequest request = Request(source, id: "sample") with
        {
            ExpectedDigest = $"sha256:{new string('0', 64)}",
        };

        await AssertCode("github-asset-changed", () =>
            catalog.ImportDownloadedGitHubIpaV2Async(request, "user:one"));
        await AssertCode("github-asset-changed", () =>
            catalog.ImportDownloadedGitHubIpaV2Async(
                Request(source, id: "sample") with
                {
                    ImmutableSourceFingerprint = $"github:42:100:200:sha256:{new string('f', 64)}",
                },
                "user:one"));

        Assert.Empty(await catalog.ListV2Async());
        Assert.Empty(FilesUnder(Path.Combine(_directory, "digest-mismatch", "imports", ".managed")));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "digest-mismatch", "imports", ".staging")));
    }

    [Fact]
    public async Task Import_RollsBackEntryReceiptAndPublishedArtifactWhenCatalogSaveFails()
    {
        string first = WriteIpa("rollback-first", "Sample.ipa", "com.example.sample", "1");
        string second = WriteIpa("rollback-second", "Sample.ipa", "com.example.sample", "2");
        FileAppCatalog catalog = CreateCatalog("rollback");
        await catalog.ImportDownloadedGitHubIpaV2Async(
            Request(first, id: "sample", idempotencyKey: "github-1"),
            "user:one");
        CatalogAppDto original = Assert.Single(await catalog.ListAsync());
        byte[] originalBytes = await File.ReadAllBytesAsync(original.IpaPath);

        string catalogPath = Path.Combine(_directory, "rollback", "catalog.json");
        File.Delete(catalogPath);
        Directory.CreateDirectory(catalogPath);

        CatalogGitHubImportRequest replacement = Request(
            second,
            id: "sample",
            assetId: 201,
            releaseTag: "v2.0",
            expectedCatalogVersion: 1,
            idempotencyKey: "github-2");
        await Assert.ThrowsAsync<CatalogStoreException>(() =>
            catalog.ImportDownloadedGitHubIpaV2Async(replacement, "user:one"));

        CatalogAppDto restored = Assert.Single(await catalog.ListAsync());
        Assert.Equal(1, restored.CatalogVersion);
        Assert.Equal(original.IpaPath, restored.IpaPath);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(restored.IpaPath));
        Assert.Single(FilesUnder(Path.Combine(_directory, "rollback", "imports", ".managed"), "*.ipa"));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "rollback"), "*.publishing"));
        Assert.Empty(FilesUnder(Path.Combine(_directory, "rollback", "imports", ".staging")));
    }

    [Fact]
    public async Task PreDownloadReplay_IsDurableAndDoesNotReadSourceOrManagedArtifact()
    {
        string source = WriteIpa("preflight", "Sample.ipa", "com.example.sample", "1");
        string digest = ComputeSha256(source);
        FileAppCatalog catalog = CreateCatalog("preflight-replay");
        CatalogGitHubImportReplayRequest replayRequest = ReplayRequest(
            id: "sample",
            digest,
            idempotencyKey: "github-1");

        Assert.Null(await catalog.TryReplayDownloadedGitHubIpaV2Async(replayRequest, "user:one"));
        Assert.Null(await catalog.TryReplayDownloadedGitHubIpaV2Async(replayRequest, "user:two"));
        await catalog.ImportDownloadedGitHubIpaV2Async(
            Request(source, id: "sample", idempotencyKey: "github-1"),
            "user:one");
        CatalogAppDto stored = Assert.Single(await catalog.ListAsync());
        File.Delete(source);
        File.Delete(stored.IpaPath);

        FileAppCatalog reloaded = CreateCatalog("preflight-replay");
        CatalogV2MutationResult? replay = await reloaded.TryReplayDownloadedGitHubIpaV2Async(
            replayRequest,
            "user:one");

        Assert.NotNull(replay);
        Assert.False(replay!.Created);
        Assert.True(replay.Replayed);
        Assert.Equal(1, replay.Entry.CatalogVersion);
        Assert.Equal(digest, replay.Entry.Sha256);
    }

    [Fact]
    public async Task PreDownloadReplay_RejectsDifferentIntentAndStaleReceiptAfterReplacement()
    {
        string first = WriteIpa("preflight-first", "Sample.ipa", "com.example.sample", "1");
        string second = WriteIpa("preflight-second", "Sample.ipa", "com.example.sample", "2");
        string firstDigest = ComputeSha256(first);
        string secondDigest = ComputeSha256(second);
        FileAppCatalog catalog = CreateCatalog("preflight-conflict");
        await catalog.ImportDownloadedGitHubIpaV2Async(
            Request(first, id: "sample", idempotencyKey: "github-1"),
            "user:one");

        CatalogGitHubImportReplayRequest original = ReplayRequest(
            id: "sample",
            firstDigest,
            idempotencyKey: "github-1");
        await AssertCode("idempotency-target-conflict", () =>
            catalog.TryReplayDownloadedGitHubIpaV2Async(
                original with { AssetId = 201 },
                "user:one"));
        await AssertCode("idempotency-target-conflict", () =>
            catalog.TryReplayDownloadedGitHubIpaV2Async(
                original with { CatalogId = "another-app" },
                "user:one"));
        await AssertCode("idempotency-target-conflict", () =>
            catalog.TryReplayDownloadedGitHubIpaV2Async(
                original with { ExpectedDigest = $"sha256:{new string('0', 64)}" },
                "user:one"));

        await catalog.ImportDownloadedGitHubIpaV2Async(
            Request(
                second,
                id: "sample",
                assetId: 201,
                releaseTag: "v2.0",
                expectedCatalogVersion: 1,
                idempotencyKey: "github-2"),
            "user:one");

        await AssertCode("idempotency-target-conflict", () =>
            catalog.TryReplayDownloadedGitHubIpaV2Async(original, "user:one"));
        CatalogV2MutationResult? current = await catalog.TryReplayDownloadedGitHubIpaV2Async(
            ReplayRequest(
                id: "sample",
                secondDigest,
                assetId: 201,
                expectedCatalogVersion: 1,
                idempotencyKey: "github-2"),
            "user:one");
        Assert.NotNull(current);
        Assert.True(current!.Replayed);
        Assert.Equal(2, current.Entry.CatalogVersion);
    }

    private CatalogGitHubImportRequest Request(
        string temporaryPath,
        string id,
        long assetId = 200,
        string releaseTag = "v1.0",
        int? expectedCatalogVersion = null,
        string? idempotencyKey = null)
    {
        string digest = ComputeSha256(temporaryPath);
        return new CatalogGitHubImportRequest(
            temporaryPath,
            "ghsrc_01",
            "dragoshont/sideport",
            100,
            assetId,
            releaseTag,
            "Sample.ipa",
            $"github:42:100:{assetId}:sha256:{digest}",
            digest.ToUpperInvariant(),
            id,
            Name: null,
            Purpose: null,
            expectedCatalogVersion,
            idempotencyKey);
    }

    private static CatalogGitHubImportReplayRequest ReplayRequest(
        string id,
        string digest,
        long assetId = 200,
        int? expectedCatalogVersion = null,
        string idempotencyKey = "github-1") =>
        new(
            "ghsrc_01",
            42,
            100,
            assetId,
            id,
            $"sha256:{digest}",
            expectedCatalogVersion,
            idempotencyKey);

    private FileAppCatalog CreateCatalog(string name) =>
        new(new AppCatalogOptions(
            Path.Combine(_directory, name, "catalog.json"),
            Path.Combine(_directory, name, "imports"),
            256 * 1024 * 1024,
            []));

    private string WriteIpa(string directoryName, string filename, string bundleId, string version)
    {
        string directory = Path.Combine(_directory, directoryName);
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

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task AssertCode(string expected, Func<Task> action)
    {
        CatalogV2Exception error = await Assert.ThrowsAsync<CatalogV2Exception>(action);
        Assert.Equal(expected, error.Code);
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
