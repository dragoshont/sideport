using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.Api.Catalog;
using Sideport.Api.GitHubCatalog;

namespace Sideport.Api.Tests;

public sealed class GitHubCatalogServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sideport-github-{Guid.NewGuid():N}");

    public GitHubCatalogServiceTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("owner/repository/extra")]
    [InlineData("owner/%2frepository")]
    [InlineData(" owner/repository")]
    [InlineData("-owner/repository")]
    public async Task Connect_RejectsAnythingExceptAnExactOwnerRepository(string repository)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        GitHubCatalogService service = CreateService(handler, BasicOptions("shape"));

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.ConnectAsync(
            new GitHubConnectionRequest(repository, "public", "shape-key"),
            "user:one"));

        Assert.Equal("github-repository-invalid", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PrivateSetup_UsesHashedActorBoundSingleUseStateAndRepositoryRestrictedToken()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-12T09:00:00Z");
        var time = new MutableTimeProvider(now);
        string keyPath = WritePrivateKey("private-setup");
        GitHubCatalogOptions options = BasicOptions("private-setup") with
        {
            AppId = 12345,
            AppSlug = "sideport-test",
            AppPrivateKeyPath = keyPath,
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/app/installations/77" => JsonResponse(new
            {
                id = 77,
                repository_selection = "selected",
                permissions = ExactPermissions(),
            }),
            "/app/installations/77/access_tokens" => TokenResponse("installation-secret", now.AddHours(1)),
            "/repos/acme/private-repo" => JsonResponse(new
            {
                id = 991L,
                full_name = "acme/private-repo",
                @private = true,
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, options, timeProvider: time);

        GitHubConnectionResult started = await service.ConnectAsync(
            new GitHubConnectionRequest("acme/private-repo", "private", "private-idempotency"),
            "user:owner");
        Assert.Equal("authorization-required", started.Connection.Status);
        Assert.NotNull(started.Connection.AuthorizationUrl);
        Uri authorization = new(started.Connection.AuthorizationUrl!);
        string rawState = QueryValue(authorization, "state");
        Assert.Equal(32, DecodeBase64Url(rawState).Length);

        string storedBefore = await File.ReadAllTextAsync(options.StatePath);
        Assert.DoesNotContain(rawState, storedBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("private-idempotency", storedBefore, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPath, storedBefore, StringComparison.Ordinal);

        // Setup survives a process restart; only the state hash was durable.
        service = CreateService(handler, options, timeProvider: time);

        GitHubSetupCallbackResult completed = await service.CompleteInstallationAsync(
            rawState, 77, "install");

        Assert.Equal("https://sideport.example", completed.RedirectUri.GetLeftPart(UriPartial.Authority));
        GitHubConnectionDto connected = (await service.GetConnectionAsync(
            started.Connection.Id, "user:owner"))!;
        Assert.Equal("connected", connected.Status);
        Assert.Equal(completed.SourceId, connected.SourceId);
        Assert.Null(await service.GetConnectionAsync(started.Connection.Id, "user:other"));

        RequestRecord tokenRequest = Assert.Single(handler.Requests, item =>
            item.Uri.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal));
        using JsonDocument tokenBody = JsonDocument.Parse(tokenRequest.Body!);
        Assert.Equal("private-repo", Assert.Single(tokenBody.RootElement.GetProperty("repositories").EnumerateArray()).GetString());
        Assert.False(tokenBody.RootElement.TryGetProperty("repository_ids", out _));
        AssertExactPermissionObject(tokenBody.RootElement.GetProperty("permissions"));

        RequestRecord installationRequest = Assert.Single(handler.Requests, item =>
            item.Uri.AbsolutePath == "/app/installations/77");
        Assert.NotNull(installationRequest.Authorization);
        Assert.StartsWith("Bearer ey", installationRequest.Authorization, StringComparison.Ordinal);
        AssertShortLivedAppJwt(installationRequest.Authorization![7..], 12345);
        RequestRecord repositoryRequest = Assert.Single(handler.Requests, item =>
            item.Uri.AbsolutePath == "/repos/acme/private-repo");
        Assert.Equal("Bearer installation-secret", repositoryRequest.Authorization);

        string storedAfter = await File.ReadAllTextAsync(options.StatePath);
        Assert.DoesNotContain("installation-secret", storedAfter, StringComparison.Ordinal);
        Assert.DoesNotContain(rawState, storedAfter, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPath, storedAfter, StringComparison.Ordinal);
        string sourcesJson = JsonSerializer.Serialize(await service.ListSourcesAsync(), WebJson);
        Assert.DoesNotContain("token", sourcesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", sourcesJson, StringComparison.OrdinalIgnoreCase);

        GitHubCatalogException replay = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(rawState, 77, "install"));
        Assert.Equal("github-state-replayed", replay.Code);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task PrivateSetup_RejectsExtraPermissionAndConsumesState()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-12T09:00:00Z");
        GitHubCatalogOptions options = BasicOptions("permissions") with
        {
            AppId = 42,
            AppSlug = "sideport-test",
            AppPrivateKeyPath = WritePrivateKey("permissions"),
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath == "/app/installations/9"
            ? JsonResponse(new
            {
                id = 9,
                repository_selection = "selected",
                permissions = new { metadata = "read", contents = "read", issues = "write" },
            })
            : throw new InvalidOperationException("No token may be minted."));
        GitHubCatalogService service = CreateService(handler, options, timeProvider: new MutableTimeProvider(now));
        GitHubConnectionResult started = await service.ConnectAsync(
            new GitHubConnectionRequest("acme/private-repo", "private", "permission-key"),
            "user:owner");
        string state = QueryValue(new Uri(started.Connection.AuthorizationUrl!), "state");

        GitHubCatalogException denied = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(state, 9, "install"));
        Assert.Equal("github-permission-insufficient", denied.Code);
        GitHubConnectionDto failed = (await service.GetConnectionAsync(started.Connection.Id, "user:owner"))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("github-permission-insufficient", failed.Error);

        GitHubCatalogException replay = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(state, 9, "install"));
        Assert.Equal("github-state-replayed", replay.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PrivateSetup_ConsumesStateAndCallsNoGitHubWhenInitiatingActorIsNoLongerAuthorized()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-12T09:00:00Z");
        GitHubCatalogOptions options = BasicOptions("actor-recheck") with
        {
            AppId = 42,
            AppSlug = "sideport-test",
            AppPrivateKeyPath = WritePrivateKey("actor-recheck"),
        };
        var authorizer = new RecordingSetupActorAuthorizer(authorized: false);
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        GitHubCatalogService service = CreateService(
            handler,
            options,
            timeProvider: new MutableTimeProvider(now),
            setupActorAuthorizer: authorizer);
        GitHubConnectionResult started = await service.ConnectAsync(
            new GitHubConnectionRequest("acme/private-repo", "private", "actor-recheck-key"),
            "member:member_replacedowner");
        string state = QueryValue(new Uri(started.Connection.AuthorizationUrl!), "state");

        GitHubCatalogException denied = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(state, 9, "install"));

        Assert.Equal("github-state-invalid", denied.Code);
        Assert.Equal("member:member_replacedowner", Assert.Single(authorizer.Actors));
        Assert.Empty(handler.Requests);
        GitHubConnectionDto failed = (await service.GetConnectionAsync(
            started.Connection.Id,
            "member:member_replacedowner"))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("github-state-invalid", failed.Error);

        GitHubCatalogException replay = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(state, 9, "install"));
        Assert.Equal("github-state-replayed", replay.Code);
        Assert.Empty(handler.Requests);
        Assert.Single(authorizer.Actors);
    }

    [Fact]
    public async Task PrivateSetup_ExpiresAfterFiveMinutesWithoutCallingGitHub()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-12T09:00:00Z");
        var time = new MutableTimeProvider(now);
        GitHubCatalogOptions options = BasicOptions("expiry") with
        {
            AppId = 42,
            AppSlug = "sideport-test",
            AppPrivateKeyPath = WritePrivateKey("expiry"),
        };
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request expected."));
        GitHubCatalogService service = CreateService(handler, options, timeProvider: time);
        GitHubConnectionResult started = await service.ConnectAsync(
            new GitHubConnectionRequest("acme/private-repo", "private", "expiry-key"),
            "user:owner");
        string state = QueryValue(new Uri(started.Connection.AuthorizationUrl!), "state");
        time.Advance(TimeSpan.FromMinutes(5));

        GitHubCatalogException expired = await Assert.ThrowsAsync<GitHubCatalogException>(() =>
            service.CompleteInstallationAsync(state, 9, "install"));

        Assert.Equal("github-state-expired", expired.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PublicSource_ValidatesIdentityAndListsOnlyNonDraftPolicyAllowedIpaAssets()
    {
        var releases = new object[]
        {
            new
            {
                id = 1,
                tag_name = "v1",
                name = "First",
                draft = false,
                prerelease = false,
                published_at = "2026-07-01T10:00:00Z",
                updated_at = "2026-07-01T10:00:00Z",
                body = "raw-body-must-not-leak",
                assets = new object[]
                {
                    new { id = 10, name = "Sideport.ipa", size = 123, state = "uploaded", updated_at = "2026-07-01T10:00:00Z", digest = (string?)null, browser_download_url = "https://secret.example/signed" },
                    new { id = 11, name = "notes.txt", size = 2, state = "uploaded", updated_at = "2026-07-01T10:00:00Z", digest = (string?)null, browser_download_url = "https://secret.example/text" },
                },
            },
            new
            {
                id = 2,
                tag_name = "draft",
                name = "Draft",
                draft = true,
                prerelease = false,
                published_at = "2026-07-01T10:00:00Z",
                updated_at = "2026-07-01T10:00:00Z",
                body = "draft-body",
                assets = Array.Empty<object>(),
            },
            new
            {
                id = 3,
                tag_name = "preview",
                name = "Preview",
                draft = false,
                prerelease = true,
                published_at = "2026-07-01T10:00:00Z",
                updated_at = "2026-07-01T10:00:00Z",
                body = "preview-body",
                assets = Array.Empty<object>(),
            },
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 55L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases" => JsonResponse(releases),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, BasicOptions("public"));
        GitHubConnectionResult connected = await service.ConnectAsync(
            new GitHubConnectionRequest("acme/public-repo", "public", "public-key"),
            "user:owner");

        GitHubReleasePageDto page = await service.ListReleasesAsync(connected.Connection.SourceId!);

        GitHubReleaseDto release = Assert.Single(page.Releases);
        GitHubReleaseAssetDto asset = Assert.Single(release.Assets);
        Assert.Equal("Sideport.ipa", asset.Name);
        string json = JsonSerializer.Serialize(page, WebJson);
        Assert.DoesNotContain("browser_download_url", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.example", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-body", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(handler.Requests, item => Assert.Equal("api.github.com", item.Uri.Host));
    }

    [Fact]
    public async Task PrepareImport_RefetchesMetadataStripsCrossHostAuthAndDeletesTemporaryFile()
    {
        byte[] ipa = CreateIpaBytes();
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(ipa)).ToLowerInvariant();
        GitHubCatalogOptions options = ConfiguredPrivateOptions("download", maxBytes: ipa.Length + 10);
        var credentials = new FixedCredentialProvider("fine-grained-secret");
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/private-repo" => JsonResponse(new { id = 88L, full_name = "acme/private-repo", @private = true }),
            "/repos/acme/private-repo/releases/101" => ReleaseResponse(101, 202, ipa.Length, digest),
            "/repos/acme/private-repo/releases/assets/202" => Redirect("https://release-assets.githubusercontent.com/download/asset"),
            "/download/asset" => BytesResponse(ipa),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, options, credentialProvider: credentials);

        GitHubPreparedImport prepared = await service.PrepareImportAsync(new GitHubCatalogImportRequest(
            "configured-private", 101, 202, "download-key", ExpectedDigest: digest));

        string temporaryPath = prepared.TemporaryIpaPath;
        Assert.EndsWith(".ipa", temporaryPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(temporaryPath));
        Assert.Equal(digest, prepared.Digest);
        Assert.EndsWith($":{digest}", prepared.ImmutableSourceFingerprint, StringComparison.Ordinal);
        string serialized = JsonSerializer.Serialize(prepared, WebJson);
        Assert.DoesNotContain("temporaryIpaPath", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temporaryPath, serialized, StringComparison.Ordinal);

        RequestRecord apiAsset = Assert.Single(handler.Requests, item =>
            item.Uri.AbsolutePath.EndsWith("/assets/202", StringComparison.Ordinal));
        Assert.Equal("Bearer fine-grained-secret", apiAsset.Authorization);
        RequestRecord redirected = Assert.Single(handler.Requests, item =>
            item.Uri.Host == "release-assets.githubusercontent.com");
        Assert.Null(redirected.Authorization);

        await prepared.DisposeAsync();
        Assert.False(File.Exists(temporaryPath));
        string state = await File.ReadAllTextAsync(options.StatePath);
        Assert.DoesNotContain("fine-grained-secret", state, StringComparison.Ordinal);
        Assert.DoesNotContain("SIDEPORT_TEST_GITHUB_TOKEN", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareImport_RejectsPrivateRedirectDestinationBeforeSendingAndCleansStaging()
    {
        byte[] ipa = CreateIpaBytes();
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(ipa)).ToLowerInvariant();
        GitHubCatalogOptions options = ConfiguredPrivateOptions("ssrf", maxBytes: ipa.Length + 10);
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/private-repo" => JsonResponse(new { id = 88L, full_name = "acme/private-repo", @private = true }),
            "/repos/acme/private-repo/releases/101" => ReleaseResponse(101, 202, ipa.Length, digest),
            "/repos/acme/private-repo/releases/assets/202" => Redirect("https://release-assets.githubusercontent.com/download/asset"),
            "/download/asset" => throw new InvalidOperationException("Private destination must not be sent."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var dns = new SelectiveDnsResolver(host => host == "release-assets.githubusercontent.com"
            ? IPAddress.Loopback
            : IPAddress.Parse("140.82.112.3"));
        GitHubCatalogService service = CreateService(
            handler, options, dns, new FixedCredentialProvider("fine-grained-secret"));

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.PrepareImportAsync(
            new GitHubCatalogImportRequest("configured-private", 101, 202, "ssrf-key", ExpectedDigest: digest)));

        Assert.Equal("github-redirect-rejected", error.Code);
        Assert.DoesNotContain(handler.Requests, item => item.Uri.Host == "release-assets.githubusercontent.com");
        Assert.Empty(FilesUnder(options.StagingDirectory));
    }

    [Fact]
    public async Task PrepareImport_RejectsOversizeMetadataWithoutDownloading()
    {
        GitHubCatalogOptions options = ConfiguredPrivateOptions("size", maxBytes: 100);
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/private-repo" => JsonResponse(new { id = 88L, full_name = "acme/private-repo", @private = true }),
            "/repos/acme/private-repo/releases/101" => ReleaseResponse(101, 202, 101, digest: null),
            "/repos/acme/private-repo/releases/assets/202" => throw new InvalidOperationException("Oversize metadata must stop before download."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(
            handler, options, credentialProvider: new FixedCredentialProvider("fine-grained-secret"));

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.PrepareImportAsync(
            new GitHubCatalogImportRequest("configured-private", 101, 202, "size-key")));

        Assert.Equal("github-asset-too-large", error.Code);
        Assert.Equal(100, error.Limit);
        Assert.DoesNotContain(handler.Requests, item => item.Uri.AbsolutePath.EndsWith("/assets/202", StringComparison.Ordinal));
        Assert.Empty(FilesUnder(options.StagingDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareImport_RejectsFourthRedirectAndNonAllowlistedLookalikeHost(bool lookalikeHost)
    {
        byte[] ipa = CreateIpaBytes();
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(ipa)).ToLowerInvariant();
        GitHubCatalogOptions options = ConfiguredPublicOptions("redirect-policy", ipa.Length + 10);
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 501L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases/101" => ReleaseResponse(101, 202, ipa.Length, digest),
            "/repos/acme/public-repo/releases/assets/202" => Redirect(lookalikeHost
                ? "https://release-assets.githubusercontent.com.evil.invalid/asset"
                : "https://release-assets.githubusercontent.com/one"),
            "/one" => Redirect("https://objects.githubusercontent.com/two"),
            "/two" => Redirect("https://github-releases.githubusercontent.com/three"),
            "/three" => Redirect("https://github.com/four"),
            "/four" => throw new InvalidOperationException("A fourth redirect must not be sent."),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, options);

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.PrepareImportAsync(
            new GitHubCatalogImportRequest("configured-public", 101, 202, "redirect-key", ExpectedDigest: digest)));

        Assert.Equal("github-redirect-rejected", error.Code);
        Assert.DoesNotContain(handler.Requests, item => item.Uri.AbsolutePath == "/four");
        Assert.DoesNotContain(handler.Requests, item => item.Uri.Host.EndsWith("evil.invalid", StringComparison.Ordinal));
        Assert.Empty(FilesUnder(options.StagingDirectory));
    }

    [Fact]
    public async Task PrepareImport_StreamByteLimitStopsUnknownLengthBodyAndCleansStaging()
    {
        const int limit = 64;
        byte[] oversized = Enumerable.Repeat((byte)0x41, limit + 1).ToArray();
        GitHubCatalogOptions options = ConfiguredPublicOptions("stream-size", limit);
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 501L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases/101" => ReleaseResponse(101, 202, limit, digest: null),
            "/repos/acme/public-repo/releases/assets/202" => UnknownLengthResponse(oversized),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, options);

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.PrepareImportAsync(
            new GitHubCatalogImportRequest("configured-public", 101, 202, "stream-size-key")));

        Assert.Equal("github-asset-too-large", error.Code);
        Assert.Equal(limit, error.Limit);
        Assert.Empty(FilesUnder(options.StagingDirectory));
    }

    [Fact]
    public async Task PrepareImport_DownloadTimeoutIsBoundedAndCleansStaging()
    {
        GitHubCatalogOptions options = ConfiguredPublicOptions("timeout", maxBytes: 10) with
        {
            DownloadTimeout = TimeSpan.FromMilliseconds(60),
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 501L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases/101" => ReleaseResponse(101, 202, 1, digest: null),
            "/repos/acme/public-repo/releases/assets/202" => BlockingResponse(),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService service = CreateService(handler, options);

        GitHubCatalogException error = await Assert.ThrowsAsync<GitHubCatalogException>(() => service.PrepareImportAsync(
            new GitHubCatalogImportRequest("configured-public", 101, 202, "timeout-key")));

        Assert.Equal("github-download-timeout", error.Code);
        Assert.Empty(FilesUnder(options.StagingDirectory));
    }

    [Fact]
    public async Task Import_ExactIdempotentReplayReturnsCurrentCatalogEntryWithoutAnyGitHubRequest()
    {
        byte[] ipa = CreateIpaBytes();
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(ipa)).ToLowerInvariant();
        GitHubCatalogOptions options = BasicOptions("replay", ipa.Length + 10) with
        {
            ConfiguredSources =
            [
                new GitHubConfiguredSource(
                    "configured-public",
                    "acme/public-repo",
                    "public",
                    RepositoryId: 501),
            ],
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 501L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases/101" => ReleaseResponse(101, 202, ipa.Length, digest),
            "/repos/acme/public-repo/releases/assets/202" => BytesResponse(ipa),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService github = CreateService(handler, options);
        var catalog = new FileAppCatalog(new AppCatalogOptions(
            Path.Combine(_directory, "replay", "catalog.json"),
            Path.Combine(_directory, "replay", "catalog-imports"),
            ipa.Length + 10,
            []));
        var importer = new GitHubCatalogImportService(github, catalog);
        var request = new GitHubCatalogImportRequest(
            "configured-public",
            101,
            202,
            "same-import-key",
            ExpectedDigest: digest,
            CatalogId: "github-test");

        CatalogV2MutationResult first = await importer.ImportAsync(request, "user:owner");
        int requestsAfterFirst = handler.Requests.Count;
        CatalogV2MutationResult replay = await importer.ImportAsync(request, "user:owner");

        Assert.True(first.Created);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Entry.Id, replay.Entry.Id);
        Assert.Equal(3, requestsAfterFirst);
        Assert.Equal(requestsAfterFirst, handler.Requests.Count);
    }

    [Fact]
    public async Task Import_ConcurrentExactReplayUsesOneDownloadChain()
    {
        byte[] ipa = CreateIpaBytes();
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(ipa)).ToLowerInvariant();
        GitHubCatalogOptions options = BasicOptions("concurrent-replay", ipa.Length + 10) with
        {
            ConfiguredSources =
            [
                new GitHubConfiguredSource(
                    "configured-public",
                    "acme/public-repo",
                    "public",
                    RepositoryId: 501),
            ],
        };
        var handler = new RecordingHandler(request => request.Uri.AbsolutePath switch
        {
            "/repos/acme/public-repo" => JsonResponse(new { id = 501L, full_name = "acme/public-repo", @private = false }),
            "/repos/acme/public-repo/releases/101" => ReleaseResponse(101, 202, ipa.Length, digest),
            "/repos/acme/public-repo/releases/assets/202" => BytesResponse(ipa),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        GitHubCatalogService github = CreateService(handler, options);
        var catalog = new FileAppCatalog(new AppCatalogOptions(
            Path.Combine(_directory, "concurrent-replay", "catalog.json"),
            Path.Combine(_directory, "concurrent-replay", "catalog-imports"),
            ipa.Length + 10,
            []));
        var importer = new GitHubCatalogImportService(github, catalog);
        var request = new GitHubCatalogImportRequest(
            "configured-public", 101, 202, "concurrent-key",
            ExpectedDigest: digest,
            CatalogId: "github-concurrent");

        CatalogV2MutationResult[] results = await Task.WhenAll(
            importer.ImportAsync(request, "user:owner"),
            importer.ImportAsync(request, "user:owner"));

        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => result.Replayed);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("64:ff9b:1::a9fe:a9fe")]
    [InlineData("2002:0a00:0001::")]
    [InlineData("2001:0000:4136:e378:8000:63bf:3fff:fdd2")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("2001:4860::5efe:10.0.0.1")]
    public void NetworkSafety_RejectsIpv6TransitionAddressesThatCanReachPrivateIpv4(string value)
    {
        Assert.False(GitHubNetworkSafety.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("140.82.112.3")]
    [InlineData("2606:50c0:8000::154")]
    public void NetworkSafety_AllowsGlobalNativeDestinations(string value)
    {
        Assert.True(GitHubNetworkSafety.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public async Task ProductionConnectCallbackRejectsMixedPrivateDnsBeforeConnecting()
    {
        GitHubCatalogOptions options = BasicOptions("handler-dns");
        var dns = new MultipleDnsResolver(
            IPAddress.Parse("140.82.112.3"),
            IPAddress.Parse("64:ff9b::a9fe:a9fe"));
        using SocketsHttpHandler handler = GitHubHttpHandlerFactory.Create(options, dns);
        Assert.NotNull(handler.ConnectCallback);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

        Exception? error = await Record.ExceptionAsync(() =>
            client.GetAsync("https://api.github.com/repos/acme/app"));

        Assert.NotNull(error);
        Assert.Contains(
            ExceptionChain(error!),
            item => item is GitHubCatalogException { Code: "github-redirect-rejected" });
    }

    [Fact]
    public void Options_RequireHttpsExceptLoopbackDevelopmentOrigins()
    {
        GitHubCatalogOptions unsafeOptions = BasicOptions("origin") with
        {
            UiBaseUri = new Uri("http://sideport.example/"),
        };
        var handler = new RecordingHandler(_ => throw new InvalidOperationException());

        Assert.Throws<ArgumentException>(() => CreateService(handler, unsafeOptions));

        GitHubCatalogOptions localOptions = unsafeOptions with { UiBaseUri = new Uri("http://127.0.0.1:6006/") };
        _ = CreateService(handler, localOptions);
    }

    private GitHubCatalogService CreateService(
        RecordingHandler handler,
        GitHubCatalogOptions options,
        IGitHubDnsResolver? dns = null,
        IGitHubCredentialProvider? credentialProvider = null,
        TimeProvider? timeProvider = null,
        IGitHubSetupActorAuthorizer? setupActorAuthorizer = null) => new(
            new GitHubCatalogStore(options.StatePath),
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            options,
            setupActorAuthorizer ?? new RecordingSetupActorAuthorizer(authorized: true),
            dns ?? new SelectiveDnsResolver(_ => IPAddress.Parse("140.82.112.3")),
            credentialProvider,
            timeProvider);

    private GitHubCatalogOptions BasicOptions(string name, long maxBytes = 16 * 1024 * 1024) => new(
        Path.Combine(_directory, name, "github.json"),
        Path.Combine(_directory, name, "staging"),
        maxBytes,
        new Uri("https://sideport.example/"));

    private GitHubCatalogOptions ConfiguredPrivateOptions(string name, long maxBytes) => BasicOptions(name, maxBytes) with
    {
        ConfiguredSources =
        [
            new GitHubConfiguredSource(
                "configured-private",
                "acme/private-repo",
                "private",
                RepositoryId: 88,
                AccessTokenEnvironmentVariable: "SIDEPORT_TEST_GITHUB_TOKEN"),
        ],
    };

    private GitHubCatalogOptions ConfiguredPublicOptions(string name, long maxBytes) => BasicOptions(name, maxBytes) with
    {
        ConfiguredSources =
        [
            new GitHubConfiguredSource(
                "configured-public",
                "acme/public-repo",
                "public",
                RepositoryId: 501),
        ],
    };

    private string WritePrivateKey(string name)
    {
        string directory = Path.Combine(_directory, name);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "github-app.pem");
        using RSA rsa = RSA.Create(2048);
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return path;
    }

    private static object ExactPermissions() => new { metadata = "read", contents = "read" };

    private static HttpResponseMessage TokenResponse(string token, DateTimeOffset expiresAt) => JsonResponse(new
    {
        token,
        expires_at = expiresAt.ToString("O"),
        permissions = ExactPermissions(),
    }, HttpStatusCode.Created);

    private static HttpResponseMessage ReleaseResponse(
        long releaseId,
        long assetId,
        long size,
        string? digest) => JsonResponse(new
        {
            id = releaseId,
            tag_name = "v1.0.0",
            name = "Version 1",
            draft = false,
            prerelease = false,
            published_at = "2026-07-01T10:00:00Z",
            updated_at = "2026-07-01T10:00:00Z",
            assets = new[]
            {
                new
                {
                    id = assetId,
                    name = "Sideport.ipa",
                    size,
                    state = "uploaded",
                    updated_at = "2026-07-01T10:00:00Z",
                    digest,
                    browser_download_url = "https://private.example/signed",
                },
            },
        });

    private static HttpResponseMessage JsonResponse(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private static HttpResponseMessage UnknownLengthResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new UnknownLengthContent(bytes),
    };

    private static HttpResponseMessage BlockingResponse() => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new BlockingReadStream()),
    };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static void AssertExactPermissionObject(JsonElement permissions)
    {
        JsonProperty[] properties = permissions.EnumerateObject().OrderBy(item => item.Name).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.Equal("contents", properties[0].Name);
        Assert.Equal("read", properties[0].Value.GetString());
        Assert.Equal("metadata", properties[1].Name);
        Assert.Equal("read", properties[1].Value.GetString());
    }

    private static void AssertShortLivedAppJwt(string jwt, long expectedIssuer)
    {
        string[] segments = jwt.Split('.');
        Assert.Equal(3, segments.Length);
        using JsonDocument payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
        long iat = payload.RootElement.GetProperty("iat").GetInt64();
        long exp = payload.RootElement.GetProperty("exp").GetInt64();
        Assert.Equal(expectedIssuer, payload.RootElement.GetProperty("iss").GetInt64());
        Assert.InRange(exp - iat, 1, 600);
    }

    private static string QueryValue(Uri uri, string name)
    {
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == name)
                return Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : string.Empty);
        }
        throw new Xunit.Sdk.XunitException($"Query parameter '{name}' was not found.");
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static byte[] CreateIpaBytes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("Payload/Test.app/Info.plist");
            using Stream target = entry.Open();
            byte[] plist = Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict>
                  <key>CFBundleIdentifier</key><string>com.example.github</string>
                  <key>CFBundleDisplayName</key><string>GitHub Test</string>
                  <key>CFBundleExecutable</key><string>GitHubTest</string>
                  <key>CFBundleVersion</key><string>1</string>
                  <key>CFBundleShortVersionString</key><string>1.0</string>
                </dict></plist>
                """);
            target.Write(plist);
        }
        return stream.ToArray();
    }

    private static IEnumerable<Exception> ExceptionChain(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            yield return current;
    }

    private static string[] FilesUnder(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record RequestRecord(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string? Body);

    private sealed class RecordingHandler(Func<RequestRecord, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RequestRecord> _requests = [];

        public IReadOnlyList<RequestRecord> Requests
        {
            get
            {
                lock (_gate)
                    return _requests.ToArray();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var record = new RequestRecord(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                body);
            lock (_gate)
                _requests.Add(record);
            HttpResponseMessage response = responder(record);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class SelectiveDnsResolver(Func<string, IPAddress> resolve) : IGitHubDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>([resolve(host)]);
        }
    }

    private sealed class MultipleDnsResolver(params IPAddress[] addresses) : IGitHubDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }
    }

    private sealed class FixedCredentialProvider(string token) : IGitHubCredentialProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(string environmentVariable, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal("SIDEPORT_TEST_GITHUB_TOKEN", environmentVariable);
            return ValueTask.FromResult<string?>(token);
        }
    }

    private sealed class RecordingSetupActorAuthorizer(bool authorized) : IGitHubSetupActorAuthorizer
    {
        public List<string> Actors { get; } = [];

        public Task<bool> IsAuthorizedAsync(string actor, CancellationToken ct = default)
        {
            Actors.Add(actor);
            return Task.FromResult(authorized);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
