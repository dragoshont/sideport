using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sideport.Api.Catalog;

namespace Sideport.Api.Tests;

public sealed class CatalogV2EndpointTests
{
    [Fact]
    public async Task ImportRoots_ReturnsConfiguredLabelsWithoutHostPaths()
    {
        string root = TestDirectory();
        using var factory = Factory(importRootPath: root, importRootLabel: "Shared apps");
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.GetAsync("/api/v2/catalog/import-roots");
        string json = await response.Content.ReadAsStringAsync();
        CatalogImportRootDto[]? roots = JsonSerializer.Deserialize<CatalogImportRootDto[]>(json, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CatalogImportRootDto configured = Assert.Single(roots!);
        Assert.Equal("shared-ipas", configured.Id);
        Assert.Equal("Shared apps", configured.Label);
        Assert.True(configured.Available);
        Assert.Equal("live", configured.Source);
        Assert.False(json.Contains(root, StringComparison.Ordinal));
        Assert.False(json.Contains("path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InspectFromRoot_CopiesArtifactAndReturnsPathFreeV2Dto()
    {
        string root = TestDirectory();
        string source = WriteTestIpa(root, "com.example.root", "Root App", "1", "1.0");
        string state = TestDirectory();
        using var factory = Factory(stateDirectory: state, importRootPath: root, importRootLabel: "Shared apps");
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v2/catalog/apps/inspect", new
        {
            rootId = "shared-ipas",
            relativePath = Path.GetFileName(source),
            id = "root-app",
            purpose = "Imported from a configured root",
            idempotencyKey = "root-import-1",
        });
        string json = await response.Content.ReadAsStringAsync();
        CatalogAppV2Dto? imported = JsonSerializer.Deserialize<CatalogAppV2Dto>(json, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(imported);
        Assert.Equal("root-app", imported!.Id);
        Assert.Equal(1, imported.CatalogVersion);
        Assert.Equal("com.example.root", imported.BundleId);
        Assert.Contains(imported.ArtifactSources, sourceDto =>
            sourceDto.Kind == "server" && sourceDto.Label == "Shared apps");
        Assert.False(json.Contains("ipaPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains(root, StringComparison.Ordinal));

        File.Delete(source);
        CatalogAppV2Dto[]? apps = await client.GetFromJsonAsync<CatalogAppV2Dto[]>("/api/v2/catalog/apps", JsonOptions);
        Assert.Contains(apps!, app => app.Id == "root-app" && app.Status == "ready");
    }

    [Fact]
    public async Task InspectFromRoot_RejectsTraversalWithPathFreeError()
    {
        string root = TestDirectory();
        using var factory = Factory(importRootPath: root);
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v2/catalog/apps/inspect", new
        {
            rootId = "shared-ipas",
            relativePath = "../secret.ipa",
        });
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("catalog-path-invalid", ErrorCode(json));
        Assert.False(json.Contains(root, StringComparison.Ordinal));
        Assert.False(json.Contains("ipaPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Upload_StoresValidIpaAndReturnsPathFreeV2Dto()
    {
        string sourceDirectory = TestDirectory();
        string ipa = WriteTestIpa(sourceDirectory, "com.example.upload", "Uploaded App", "7", "1.2.3");
        string state = TestDirectory();
        using var factory = Factory(stateDirectory: state);
        using HttpClient client = AuthenticatedClient(factory);
        using MultipartFormDataContent content = UploadContent(
            ipa,
            id: "uploaded-app",
            idempotencyKey: "upload-1");

        HttpResponseMessage response = await client.PostAsync("/api/v2/catalog/apps/upload", content);
        string json = await response.Content.ReadAsStringAsync();
        CatalogAppV2Dto? uploaded = JsonSerializer.Deserialize<CatalogAppV2Dto>(json, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(uploaded);
        Assert.Equal("uploaded-app", uploaded!.Id);
        Assert.Equal("com.example.upload", uploaded.BundleId);
        Assert.Contains(uploaded.ArtifactSources, sourceDto => sourceDto.Kind == "browser-upload");
        Assert.False(json.Contains("ipaPath", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains(state, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_DoesNotHonorV1ReplaceShortcutAndRequiresCurrentVersion()
    {
        string first = WriteTestIpa(Path.Combine(TestDirectory(), "first"), "com.example.replace", "App", "1", "1.0");
        string second = WriteTestIpa(Path.Combine(TestDirectory(), "second"), "com.example.replace", "App", "2", "2.0");
        using var factory = Factory();
        using HttpClient client = AuthenticatedClient(factory);
        using (MultipartFormDataContent content = UploadContent(first, id: "replace-app"))
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/v2/catalog/apps/upload", content)).StatusCode);

        using MultipartFormDataContent withoutVersion = UploadContent(second, id: "replace-app", replace: true);
        HttpResponseMessage conflict = await client.PostAsync("/api/v2/catalog/apps/upload", withoutVersion);
        string conflictJson = await conflict.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("catalog-version-conflict", ErrorCode(conflictJson));

        using MultipartFormDataContent withVersion = UploadContent(second, id: "replace-app", expectedCatalogVersion: 1);
        HttpResponseMessage updated = await client.PostAsync("/api/v2/catalog/apps/upload", withVersion);
        CatalogAppV2Dto? updatedApp = await updated.Content.ReadFromJsonAsync<CatalogAppV2Dto>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, updatedApp!.CatalogVersion);
    }

    [Fact]
    public async Task Upload_EnforcesConfiguredLimitDuringCopy()
    {
        string ipa = WriteTestIpa(TestDirectory(), "com.example.large", "Large App", "1", "1.0");
        string state = TestDirectory();
        using var factory = Factory(stateDirectory: state, maxUploadBytes: 10);
        using HttpClient client = AuthenticatedClient(factory);
        using MultipartFormDataContent content = UploadContent(ipa, id: "too-large");

        HttpResponseMessage response = await client.PostAsync("/api/v2/catalog/apps/upload", content);
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("upload-too-large", ErrorCode(json));
        Assert.Contains("\"limit\":10", json, StringComparison.Ordinal);
        Assert.False(json.Contains(state, StringComparison.Ordinal));
        CatalogAppV2Dto[]? apps = await client.GetFromJsonAsync<CatalogAppV2Dto[]>("/api/v2/catalog/apps", JsonOptions);
        Assert.DoesNotContain(apps!, app => app.Id == "too-large");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> Factory(
        string? stateDirectory = null,
        string? importRootPath = null,
        string importRootLabel = "Shared apps",
        long? maxUploadBytes = null)
    {
        stateDirectory ??= TestDirectory();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", typeof(CatalogV2EndpointTests).Assembly.Location);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            builder.UseSetting("Sideport:Api:AuthToken", "catalog-test-token");
            if (maxUploadBytes is not null)
                builder.UseSetting("Sideport:Catalog:MaxUploadBytes", maxUploadBytes.Value.ToString());
            if (importRootPath is not null)
            {
                builder.UseSetting("Sideport:Catalog:ImportRoots:0:Id", "shared-ipas");
                builder.UseSetting("Sideport:Catalog:ImportRoots:0:Label", importRootLabel);
                builder.UseSetting("Sideport:Catalog:ImportRoots:0:Path", importRootPath);
            }

            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "catalog-test-token");
        return client;
    }

    private static MultipartFormDataContent UploadContent(
        string path,
        string? id = null,
        string? idempotencyKey = null,
        int? expectedCatalogVersion = null,
        bool replace = false)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(File.OpenRead(path)), "ipa", Path.GetFileName(path));
        if (id is not null)
            content.Add(new StringContent(id), "id");
        if (idempotencyKey is not null)
            content.Add(new StringContent(idempotencyKey), "idempotencyKey");
        if (expectedCatalogVersion is not null)
            content.Add(new StringContent(expectedCatalogVersion.Value.ToString()), "expectedCatalogVersion");
        if (replace)
            content.Add(new StringContent("true"), "replace");
        return content;
    }

    private static string ErrorCode(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("error").GetString()!;
    }

    private static string TestDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sideport-catalog-v2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string WriteTestIpa(
        string directory,
        string bundleId,
        string displayName,
        string version,
        string shortVersion)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{bundleId}.ipa");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("Payload/Test.app/Info.plist");
        using Stream entryStream = entry.Open();
        byte[] plist = Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleIdentifier</key><string>{{bundleId}}</string>
                <key>CFBundleDisplayName</key><string>{{displayName}}</string>
                <key>CFBundleExecutable</key><string>Test</string>
                <key>CFBundleVersion</key><string>{{version}}</string>
                <key>CFBundleShortVersionString</key><string>{{shortVersion}}</string>
            </dict>
            </plist>
            """);
        entryStream.Write(plist);
        return path;
    }
}
