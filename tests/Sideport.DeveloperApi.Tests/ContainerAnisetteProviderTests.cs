using System.Net;
using System.Text;
using Sideport.Core;

namespace Sideport.DeveloperApi.Tests;

/// <summary>
/// Tests <see cref="ContainerAnisetteProvider"/> against the flat "v1" anisette
/// contract (<c>GET /</c>), including the device-identity fields Sideport reuses
/// to inherit an existing anisette/AltServer trust.
/// </summary>
public class ContainerAnisetteProviderTests
{
    private static ContainerAnisetteProvider Build(string rootJson, string? clientInfoJson = null)
    {
        var handler = new StubHandler(rootJson, clientInfoJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://anisette.test/") };
        return new ContainerAnisetteProvider(http);
    }

    [Fact]
    public async Task GetHeaders_ParsesFlatV1HeaderSet_IncludingDeviceIdentity()
    {
        // The shape Dadoum's anisette-v3-server returns from GET / — the same set
        // AltServer authenticates with on the homelab host.
        const string root = """
        {
          "X-Apple-I-MD": "AAAABQAAABANLIQSyz7sJHufOW",
          "X-Apple-I-MD-M": "28xTliWDWazO6+GkZxli0JYiChO",
          "X-Apple-I-MD-LU": "A8B3651C4326AA4824B39B6C7471",
          "X-Apple-I-MD-RINFO": "17106176",
          "X-Mme-Device-Id": "BAC92E5F-5824-4CBD-A77F-29FDA8093EFD",
          "X-MMe-Client-Info": "<MacBookPro13,2> <macOS;13.1;22C65>"
        }
        """;

        AnisetteHeaders headers = await Build(root).GetHeadersAsync();

        Assert.Equal("AAAABQAAABANLIQSyz7sJHufOW", headers.OneTimePassword);
        Assert.Equal("28xTliWDWazO6+GkZxli0JYiChO", headers.MachineId);
        Assert.Equal("A8B3651C4326AA4824B39B6C7471", headers.LocalUserId);
        Assert.Equal("17106176", headers.RoutingInfo);
        Assert.Equal("BAC92E5F-5824-4CBD-A77F-29FDA8093EFD", headers.DeviceId);
        Assert.Equal("<MacBookPro13,2> <macOS;13.1;22C65>", headers.ClientInfo);
    }

    [Fact]
    public async Task GetHeaders_NumericRoutingInfo_IsCoercedToString()
    {
        const string root = """
        { "X-Apple-I-MD": "md", "X-Apple-I-MD-RINFO": 17106176 }
        """;
        AnisetteHeaders headers = await Build(root).GetHeadersAsync();
        Assert.Equal("17106176", headers.RoutingInfo);
    }

    [Fact]
    public async Task GetHeaders_NoOneTimePassword_ThrowsProvisioningHint()
    {
        // Mirrors the "not provisioned (-45061)" failure: a healthy server reachable
        // but the ADI not yet provisioned. The error must point at the ADI volume.
        const string root = """{ "X-Apple-I-MD-M": "machine-only" }""";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(root).GetHeadersAsync());
        Assert.Contains("provision", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetClientInfo_ReadsV3ClientInfo()
    {
        const string root = """{ "X-Apple-I-MD": "md" }""";
        const string clientInfo = """
        { "client_info": "<MacBookPro13,2> <macOS;13.1;22C65>", "user_agent": "akd/1.0" }
        """;
        AnisetteClientInfo info = await Build(root, clientInfo).GetClientInfoAsync();
        Assert.Equal("<MacBookPro13,2> <macOS;13.1;22C65>", info.ClientInfo);
        Assert.Equal("akd/1.0", info.UserAgent);
    }

    private sealed class StubHandler(string rootJson, string? clientInfoJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            string body = path.Contains("client_info")
                ? clientInfoJson ?? "{}"
                : rootJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
