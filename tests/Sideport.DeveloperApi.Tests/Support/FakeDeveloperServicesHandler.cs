using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Claunia.PropertyList;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that implements the
/// <c>developerservices2.apple.com</c> plist "action" protocol on top of an
/// in-memory team/device/cert/App-ID/profile store. It parses the real request
/// plists <see cref="DeveloperServices.DeveloperServicesClient"/> sends and
/// returns the real response plists the portal expects — exercising the request
/// envelope, identity + anisette headers, plist encode/decode, gzip inflate, the
/// resultCode error envelope, and the CSR → certificate → profile flow end to
/// end without touching Apple.
/// </summary>
internal sealed class FakeDeveloperServicesHandler : HttpMessageHandler
{
    private readonly string _teamId;
    private readonly HashSet<string> _devices = [];
    private readonly Dictionary<string, string> _appIds = []; // bundleId -> appIdId
    private int _certSerial;
    private int _appIdSeq;

    /// <summary>Bundle id the team-provisioning profile will be issued for.</summary>
    public string ExpiryBundleId { get; private set; } = "";

    /// <summary>How long issued certificates/profiles are valid.</summary>
    public TimeSpan CertificateLifetime { get; init; } = TimeSpan.FromDays(365);
    public TimeSpan ProfileLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Captured (action, requestDict, headers) per call, in order.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>When set, the next matching action returns this Apple resultCode.</summary>
    public (string Action, long Code, string Message)? NextError { get; set; }

    public FakeDeveloperServicesHandler(string teamId = "ABCDE12345")
    {
        _teamId = teamId;
    }

    public sealed record CapturedRequest(
        string Action, NSDictionary Body, IReadOnlyDictionary<string, string> Headers);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string action = request.RequestUri!.AbsolutePath.Split("/QH65B2/", 2)[^1];
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var requestDict = (NSDictionary)PropertyListParser.Parse(body);

        var headers = request.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new CapturedRequest(action, requestDict, headers));

        if (NextError is { } err && err.Action == action)
        {
            NextError = null;
            return Ok(PlistContent(ErrorResponse(err.Code, err.Message)));
        }

        NSDictionary response = action switch
        {
            "listTeams.action" => ListTeams(),
            "ios/listDevices.action" => ListDevices(),
            "ios/addDevice.action" => AddDevice(requestDict),
            "ios/submitDevelopmentCSR.action" => SubmitCsr(requestDict),
            "ios/listAppIds.action" => ListAppIds(),
            "ios/addAppId.action" => AddAppId(requestDict),
            "ios/downloadTeamProvisioningProfile.action" => DownloadProfile(requestDict),
            _ => ErrorResponse(9999, $"unknown action {action}"),
        };

        return Ok(PlistContent(response));
    }

    // --- actions -----------------------------------------------------------

    private NSDictionary ListTeams()
    {
        var team = new NSDictionary();
        team.Add("teamId", _teamId);
        team.Add("name", "Test Team");
        team.Add("type", "Individual");

        var response = Ok0();
        response.Add("teams", new NSArray(team));
        return response;
    }

    private NSDictionary ListDevices()
    {
        var array = new NSArray([.. _devices.Select(udid =>
        {
            var d = new NSDictionary();
            d.Add("deviceNumber", udid);
            d.Add("name", "Test Device");
            d.Add("deviceClass", "IPHONE");
            return (NSObject)d;
        })]);

        var response = Ok0();
        response.Add("devices", array);
        return response;
    }

    private NSDictionary AddDevice(NSDictionary request)
    {
        string udid = request["deviceNumber"].ToString()!;
        _devices.Add(udid);

        var device = new NSDictionary();
        device.Add("deviceNumber", udid);
        device.Add("name", request["name"].ToString());
        device.Add("deviceClass", "IPHONE");

        var response = Ok0();
        response.Add("device", device);
        return response;
    }

    private NSDictionary SubmitCsr(NSDictionary request)
    {
        // Parse the PEM CSR the portal sent, mint a self-signed cert for it.
        string csrPem = request["csrContent"].ToString()!;
        byte[] certDer = IssueCertificate(csrPem);

        var certRequest = new NSDictionary();
        certRequest.Add("certContent", new NSData(certDer));
        certRequest.Add("name", "Sideport Development");
        certRequest.Add("serialNumber", (++_certSerial).ToString());

        var response = Ok0();
        response.Add("certRequest", certRequest);
        return response;
    }

    private NSDictionary ListAppIds()
    {
        var array = new NSArray([.. _appIds.Select(kvp =>
        {
            var a = new NSDictionary();
            a.Add("appIdId", kvp.Value);
            a.Add("identifier", kvp.Key);
            a.Add("name", "Sideport App");
            return (NSObject)a;
        })]);

        var response = Ok0();
        response.Add("appIds", array);
        return response;
    }

    private NSDictionary AddAppId(NSDictionary request)
    {
        string bundleId = request["identifier"].ToString()!;
        string appIdId = $"APPID{++_appIdSeq}";
        _appIds[bundleId] = appIdId;

        var appId = new NSDictionary();
        appId.Add("appIdId", appIdId);
        appId.Add("identifier", bundleId);
        appId.Add("name", request["name"].ToString());

        var response = Ok0();
        response.Add("appId", appId);
        return response;
    }

    private NSDictionary DownloadProfile(NSDictionary request)
    {
        string appIdId = request["appIdId"].ToString()!;
        string bundleId = _appIds.FirstOrDefault(kvp => kvp.Value == appIdId).Key
            ?? throw new InvalidOperationException("profile requested for unknown appIdId");
        ExpiryBundleId = bundleId;

        byte[] mobileProvision = TestMobileProvisionBuilder.Build(
            name: "Sideport Profile",
            teamName: "Test Team",
            teamId: _teamId,
            applicationIdentifier: $"{_teamId}.{bundleId}",
            expiration: DateTimeOffset.UtcNow + ProfileLifetime);

        var profile = new NSDictionary();
        profile.Add("provisioningProfileId", "PROFILE123");
        profile.Add("encodedProfile", new NSData(mobileProvision));

        var response = Ok0();
        response.Add("provisioningProfile", profile);
        return response;
    }

    // --- cert issuance -----------------------------------------------------

    private byte[] IssueCertificate(string csrPem)
    {
        // Apple signs the CSR's public key into a certificate; mirror that so the
        // issued cert's public key matches the caller's private key (a fresh key
        // would fail CopyWithPrivateKey). Issue a leaf carrying the CSR's public
        // key, signed by a throwaway CA.
        CertificateRequest csr = CertificateRequest.LoadSigningRequestPem(
            csrPem, HashAlgorithmName.SHA256,
            signerSignaturePadding: RSASignaturePadding.Pkcs1);

        using RSA caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=Sideport Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, true));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ca = caRequest.CreateSelfSigned(
            now.AddDays(-1), now + CertificateLifetime + TimeSpan.FromDays(1));

        var leafRequest = new CertificateRequest(
            new System.Security.Cryptography.X509Certificates.X500DistinguishedName(
                "CN=Sideport Development"),
            csr.PublicKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        byte[] serial = BitConverter.GetBytes((long)(_certSerial + 1));
        using X509Certificate2 leaf = leafRequest.Create(ca, now, now + CertificateLifetime, serial);
        return leaf.Export(X509ContentType.Cert);
    }

    // --- helpers -----------------------------------------------------------

    private static NSDictionary Ok0()
    {
        var d = new NSDictionary();
        d.Add("resultCode", 0);
        return d;
    }

    private static NSDictionary ErrorResponse(long code, string message)
    {
        var d = new NSDictionary();
        d.Add("resultCode", code);
        d.Add("userString", message);
        return d;
    }

    private static HttpContent PlistContent(NSDictionary dict)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(dict.ToXmlPropertyList());
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/x-xml-plist");
        return content;
    }

    private static HttpResponseMessage Ok(HttpContent content) =>
        new(HttpStatusCode.OK) { Content = content };
}
