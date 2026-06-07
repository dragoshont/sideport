using System.Net;
using Claunia.PropertyList;
using Sideport.GrandSlam.Crypto;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that implements the GrandSlam
/// GsService2 wire protocol on top of <see cref="GrandSlamSrpServer"/>, so the
/// real <c>GrandSlamClient</c> can be driven through a complete login (and 2FA)
/// without touching Apple. It parses the actual request plists the client sends
/// and returns the actual response plists the client expects — exercising SRP,
/// plist encode/decode, and SPD CBC decryption end-to-end.
/// </summary>
internal sealed class FakeGrandSlamHandler : HttpMessageHandler
{
    private readonly string _username;
    private readonly byte[] _passwordKey;
    private readonly byte[] _salt;
    private readonly int _iterations;
    private readonly string _adsid;
    private readonly string _idmsToken;
    private readonly string _accountName;

    private GrandSlamSrpServer? _server;
    private TwoFactorMode _twoFactor;
    private byte[]? _clientA;

    // The app-token GCM session key delivered in the SPD (deterministic for tests).
    private readonly byte[] _appSk = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    /// <summary>The app token the apptokens <c>et</c> blob will carry.</summary>
    public string AppToken { get; init; } = "fake-app-token";

    /// <summary>How the server should respond to the first <c>complete</c> round.</summary>
    public enum TwoFactorMode
    {
        /// <summary>No second factor — return a usable session immediately.</summary>
        None,

        /// <summary>Demand a trusted-device code on the first attempt.</summary>
        TrustedDevice,

        /// <summary>Demand an SMS code on the first attempt.</summary>
        Sms,
    }

    /// <summary>Whether the trusted-device prompt endpoint was hit.</summary>
    public bool TrustedDevicePromptTriggered { get; private set; }

    /// <summary>Whether a 2FA code was validated.</summary>
    public bool CodeValidated { get; private set; }

    /// <summary>The code the validate endpoint will accept (others are rejected).</summary>
    public string ExpectedCode { get; init; } = "123456";

    /// <summary>
    /// The SRP password-stretch protocol the server advertises in <c>sp</c>
    /// (<c>"s2k"</c> or <c>"s2k_fo"</c>). The supplied password key must be
    /// derived with the matching hex-expand setting.
    /// </summary>
    public string Protocol { get; init; } = "s2k";

    public FakeGrandSlamHandler(
        string username,
        byte[] passwordKey,
        byte[] salt,
        int iterations,
        TwoFactorMode twoFactor = TwoFactorMode.None,
        string adsid = "000123-04-deadbeef",
        string idmsToken = "test-idms-token",
        string accountName = "Test Person")
    {
        _username = username;
        _passwordKey = passwordKey;
        _salt = salt;
        _iterations = iterations;
        _twoFactor = twoFactor;
        _adsid = adsid;
        _idmsToken = idmsToken;
        _accountName = accountName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.AbsoluteUri;

        if (url.Contains("/auth/verify/trusteddevice"))
        {
            TrustedDevicePromptTriggered = true;
            return Ok(new ByteArrayContent("<html>prompt</html>"u8.ToArray()));
        }

        if (url.Contains("/GsService2/validate"))
            return ValidateCode(request);

        if (url.Contains("/grandslam/GsService2"))
            return await HandleSrpAsync(request, cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleSrpAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        byte[] requestBytes = await request.Content!.ReadAsByteArrayAsync(ct);
        var body = (NSDictionary)PropertyListParser.Parse(requestBytes);
        var requestDict = (NSDictionary)body["Request"];
        string operation = requestDict["o"].ToString()!;

        return operation switch
        {
            "init" => Ok(PlistContent(BuildInitResponse(requestDict))),
            "complete" => Ok(PlistContent(BuildCompleteResponse(requestDict))),
            "apptokens" => Ok(PlistContent(BuildAppTokensResponse())),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
        };
    }

    private NSDictionary BuildInitResponse(NSDictionary request)
    {
        // A arrives in the init round and is held across to complete (the server
        // is stateful across the two rounds, as Apple's is via the cookie).
        _clientA = ((NSData)request["A2k"]).Bytes;
        _server = new GrandSlamSrpServer(_username, _passwordKey, _salt, _iterations);
        byte[] b = _server.StartChallenge();

        var response = new NSDictionary();
        response.Add("sp", Protocol);
        response.Add("s", new NSData(_salt));
        response.Add("i", _iterations);
        response.Add("B", new NSData(b));
        // Real GSA sends the cookie as a plist string (not data); mirror that so
        // the replay oracle is faithful to the live wire format.
        response.Add("c", "server-cookie");
        response.Add("Status", Status(ec: 0));
        return Wrap(response);
    }

    private NSDictionary BuildCompleteResponse(NSDictionary request)
    {
        if (_server is null || _clientA is null)
            throw new InvalidOperationException("complete called before init");

        byte[] clientM1 = ((NSData)request["M1"]).Bytes;
        byte[] m2;
        try
        {
            m2 = _server.CompleteHandshake(_clientA, clientM1);
        }
        catch (InvalidOperationException)
        {
            // Wrong password: the client's M1 doesn't verify. Apple signals this
            // with a non-zero error status, not a transport failure.
            var error = new NSDictionary();
            error.Add("Status", Status(ec: -22406, em: "Unable to sign in (wrong password)"));
            return Wrap(error);
        }

        // Build the SPD plist and encrypt it with independently-derived keys.
        var spd = new NSDictionary();
        spd.Add("adsid", _adsid);
        spd.Add("GsIdmsToken", _idmsToken);
        spd.Add("acname", _accountName);
        // The SPD also carries the app-token GCM session key (sk) + a cookie (c),
        // both as data — the client uses them to fetch the dev-API app token.
        spd.Add("sk", new NSData(_appSk));
        spd.Add("c", new NSData("apptoken-cookie"u8.ToArray()));
        // Real GSA returns the SPD as a BARE <dict>…</dict> fragment (no <?xml?>,
        // no DOCTYPE, no <plist> envelope) — verified live. Strip the envelope so
        // the replay oracle exercises the same fragment-parse path the client must
        // handle against Apple.
        byte[] spdPlain = System.Text.Encoding.UTF8.GetBytes(ExtractBareDict(spd.ToXmlPropertyList()));
        byte[] spdCipher = _server.EncryptSpd(spdPlain);

        var response = new NSDictionary();
        response.Add("M2", new NSData(m2));
        response.Add("spd", new NSData(spdCipher));

        NSDictionary status = _twoFactor switch
        {
            TwoFactorMode.TrustedDevice => Status(ec: 0, au: "trustedDeviceSecondaryAuth"),
            TwoFactorMode.Sms => Status(ec: 0, au: "secondaryAuth"),
            _ => Status(ec: 0),
        };
        response.Add("Status", status);
        return Wrap(response);
    }

    private HttpResponseMessage ValidateCode(HttpRequestMessage request)
    {
        string? code = request.Headers.TryGetValues("security-code", out var values)
            ? values.FirstOrDefault()
            : null;

        // The validate endpoint returns Status at the top level (not wrapped in
        // "Response"), matching the documented GsService2/validate shape.
        var response = new NSDictionary();
        if (code == ExpectedCode)
        {
            CodeValidated = true;
            // After successful validation, subsequent logins skip the factor.
            _twoFactor = TwoFactorMode.None;
            response.Add("Status", Status(ec: 0));
        }
        else
        {
            response.Add("Status", Status(ec: -28000, em: "wrong code"));
        }

        return Ok(PlistContent(response));
    }

    private NSDictionary Wrap(NSDictionary inner)
    {
        var root = new NSDictionary();
        root.Add("Response", inner);
        return root;
    }

    /// <summary>
    /// Reduce a full plist document to its bare <c>&lt;dict&gt;…&lt;/dict&gt;</c>
    /// fragment — the shape real GrandSlam returns for the SPD blob.
    /// </summary>
    private static string ExtractBareDict(string fullPlistXml)
    {
        int start = fullPlistXml.IndexOf("<dict", StringComparison.Ordinal);
        int end = fullPlistXml.LastIndexOf("</dict>", StringComparison.Ordinal);
        return start >= 0 && end > start
            ? fullPlistXml[start..(end + "</dict>".Length)]
            : fullPlistXml;
    }

    /// <summary>
    /// Build the <c>apptokens</c> response: a GCM-encrypted <c>et</c> blob
    /// (<c>[3B "XYZ"][16B IV][ct][16B tag]</c>) carrying
    /// <c>{ t: { com.apple.gs.xcode.auth: { token: AppToken } } }</c>, encrypted
    /// under the SPD session key — the same shape the client decrypts.
    /// </summary>
    private NSDictionary BuildAppTokensResponse()
    {
        var appEntry = new NSDictionary();
        appEntry.Add("token", AppToken);
        var apps = new NSDictionary();
        apps.Add("com.apple.gs.xcode.auth", appEntry);
        var tokenPlist = new NSDictionary();
        tokenPlist.Add("t", apps);
        byte[] tokenBytes = System.Text.Encoding.UTF8.GetBytes(tokenPlist.ToXmlPropertyList());

        byte[] iv = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();
        byte[] aad = "XYZ"u8.ToArray();
        byte[] tag = new byte[16];
        byte[] ciphertext = GrandSlamCipher.EncryptGcm(_appSk, iv, tokenBytes, tag, aad);

        byte[] et = new byte[3 + 16 + ciphertext.Length + 16];
        aad.CopyTo(et, 0);
        iv.CopyTo(et, 3);
        ciphertext.CopyTo(et, 3 + 16);
        tag.CopyTo(et, 3 + 16 + ciphertext.Length);

        var response = new NSDictionary();
        response.Add("et", new NSData(et));
        response.Add("Status", Status(ec: 0));
        return Wrap(response);
    }

    private static NSDictionary Status(long ec, string? em = null, string? au = null)
    {
        var status = new NSDictionary();
        status.Add("ec", ec);
        if (em is not null) status.Add("em", em);
        if (au is not null) status.Add("au", au);
        return status;
    }

    private static HttpContent PlistContent(NSDictionary dict)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(dict.ToXmlPropertyList());
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/x-xml-plist");
        return content;
    }

    private HttpResponseMessage Ok(HttpContent content) =>
        new(HttpStatusCode.OK) { Content = content };
}
