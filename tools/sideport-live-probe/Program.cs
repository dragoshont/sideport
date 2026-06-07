using Claunia.PropertyList;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Plist;

// ─────────────────────────────────────────────────────────────────────────────
// Sideport live read-path probe.
//
// Authenticates against the REAL Apple GrandSlam service and exercises the
// READ-ONLY developer-services actions (listTeams / listDevices / listAppIds).
// These consume ZERO free-tier quota and do NOT mint a certificate, so they are
// safe to run alongside a live AltServer (no single-signer conflict). It never
// calls submitDevelopmentCSR / addAppId / addDevice.
//
// Credentials come from the environment (the host's /etc/altserver/env), never
// the command line:
//   SIDEPORT_APPLE_ID, SIDEPORT_APPLE_PW           (required)
//   SIDEPORT_ANISETTE_URL   (default http://127.0.0.1:6969)
//   SIDEPORT_DEVICE_ID      (fallback only; anisette's device-id wins if present)
// ─────────────────────────────────────────────────────────────────────────────

string appleId = Env("SIDEPORT_APPLE_ID");
string password = Env("SIDEPORT_APPLE_PW");
string anisetteUrl = Environment.GetEnvironmentVariable("SIDEPORT_ANISETTE_URL") ?? "http://127.0.0.1:6969";
string deviceIdFallback = Environment.GetEnvironmentVariable("SIDEPORT_DEVICE_ID") ?? "00000000-0000000000000000";

Console.WriteLine($"== Sideport live read-path probe ==");
Console.WriteLine($"   apple id : {Redact(appleId)}");
Console.WriteLine($"   anisette : {anisetteUrl}");

var options = new GrandSlamClientOptions { DeviceId = deviceIdFallback };

// Console logging at Debug so the dev-api error responses surface.
using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));

// Anisette provider (flat v1 GET / — the trusted, provisioned path).
var anisetteHttp = new HttpClient { BaseAddress = new Uri(anisetteUrl) };
var anisette = new ContainerAnisetteProvider(anisetteHttp);

// Sanity-check anisette before touching Apple.
AnisetteHeaders sample = await anisette.GetHeadersAsync();
Console.WriteLine($"   anisette device-id : {sample.DeviceId} (empty => fallback {deviceIdFallback})");
Console.WriteLine($"   anisette client    : {sample.ClientInfo}");

// GrandSlam client (Apple-CA pinned, like production).
var gsaHttp = new HttpClient(new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, cert, chain, _) => AppleCaPinning.Validate(cert, chain),
});
var grandSlam = new GrandSlamClient(gsaHttp, anisette, options, loggerFactory.CreateLogger<GrandSlamClient>());

// developerservices2 client (normal public-trust TLS). With SIDEPORT_PROBE_WIRE=1
// the exact request headers + full response body are dumped (token/OTP redacted)
// so the live wire format can be diffed against the AltServer/AltSign control.
bool wire = Environment.GetEnvironmentVariable("SIDEPORT_PROBE_WIRE") == "1";
HttpMessageHandler devInner = new HttpClientHandler();
var devHttp = new HttpClient(wire ? new ProbeWireHandler(devInner) : devInner);
var dev = new DeveloperServicesClient(devHttp, anisette, options, loggerFactory.CreateLogger<DeveloperServicesClient>());

// ── 1. Authenticate ──────────────────────────────────────────────────────────
Console.WriteLine("\n[1] GrandSlam authenticate…");
AppleLoginResult login = await grandSlam.AuthenticateAsync(appleId, password);
if (login is AppleLoginResult.TwoFactorRequired twoFactor)
{
    Console.WriteLine($"    2FA REQUIRED ({twoFactor.Challenge.Kind}). Trust was NOT inherited from anisette.");
    Console.WriteLine("    (A code was triggered; this probe does not submit it. Re-run after the device is trusted.)");
    return 2;
}
var session = ((AppleLoginResult.Success)login).Session;
Console.WriteLine($"    OK — adsid {Redact(session.Adsid)}, account '{session.AccountName}', idms {(string.IsNullOrEmpty(session.IdmsToken) ? "MISSING" : "present")}");
Console.WriteLine($"    app-token: len={session.IdmsToken.Length} prefix='{Sanitize(session.IdmsToken)}'");

// ── 2. listTeams (read) ──────────────────────────────────────────────────────
Console.WriteLine("\n[2] listTeams…");
NSDictionary teamsResp = await dev.SendActionAsync(
    DeveloperServicesEndpoints.ListTeams, session, teamId: null, parameters: null);
var teams = new List<(string id, string name, string type)>();
foreach (NSObject item in PlistCodec.GetArrayOrEmpty(teamsResp, "teams"))
    if (item is NSDictionary t)
        teams.Add((PlistCodec.GetStringOrNull(t, "teamId") ?? "?",
                   PlistCodec.GetStringOrNull(t, "name") ?? "?",
                   PlistCodec.GetStringOrNull(t, "type") ?? "?"));
Console.WriteLine($"    OK — {teams.Count} team(s):");
foreach (var t in teams)
    Console.WriteLine($"      - {t.id}  {t.name}  ({t.type})");
if (teams.Count == 0) { Console.WriteLine("    no teams — cannot continue device/appId reads"); return 3; }
string teamId = teams[0].id;

// ── 3. listDevices (read) ────────────────────────────────────────────────────
Console.WriteLine($"\n[3] listDevices (team {teamId})…");
NSDictionary devResp = await dev.SendActionAsync(
    DeveloperServicesEndpoints.ListDevices, session, teamId, parameters: null);
int deviceCount = 0;
foreach (NSObject item in PlistCodec.GetArrayOrEmpty(devResp, "devices"))
{
    if (item is not NSDictionary d) continue;
    deviceCount++;
    if (deviceCount <= 10)
        Console.WriteLine($"      - {PlistCodec.GetStringOrNull(d, "deviceNumber")}  {PlistCodec.GetStringOrNull(d, "name")}  [{PlistCodec.GetStringOrNull(d, "deviceClass")}]");
}
Console.WriteLine($"    OK — {deviceCount} device(s)");

// ── 4. listAppIds (read) ─────────────────────────────────────────────────────
Console.WriteLine($"\n[4] listAppIds (team {teamId})…");
NSDictionary appResp = await dev.SendActionAsync(
    DeveloperServicesEndpoints.ListAppIds, session, teamId, parameters: null);
int appCount = 0;
foreach (NSObject item in PlistCodec.GetArrayOrEmpty(appResp, "appIds"))
{
    if (item is not NSDictionary a) continue;
    appCount++;
    if (appCount <= 20)
        Console.WriteLine($"      - {PlistCodec.GetStringOrNull(a, "identifier")}  ({PlistCodec.GetStringOrNull(a, "name")})  id={PlistCodec.GetStringOrNull(a, "appIdId")}");
}
Console.WriteLine($"    OK — {appCount} app id(s)");

Console.WriteLine("\n== READ-PATH VALIDATION PASSED ==");
Console.WriteLine("   auth + listTeams + listDevices + listAppIds all succeeded over the live");
Console.WriteLine("   wire format. No quota consumed, no certificate minted.");
return 0;

static string Env(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"required env var {name} is not set");

static string Redact(string value) =>
    string.IsNullOrEmpty(value) || value.Length <= 4 ? "***" : value[..4] + "***";

// Show enough of a token to compare shape/encoding without leaking it whole.
static string Sanitize(string value) =>
    string.IsNullOrEmpty(value) ? "(empty)"
    : value.Length <= 16 ? value[..2] + "…" + value[^2..]
    : value[..8] + "…" + value[^6..];

// Dumps the developerservices2 request headers (sensitive ones redacted to a
// length+shape so the wire format is comparable to the AltSign control without
// leaking the token/OTP) and the full decompressed response body.
sealed class ProbeWireHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private static readonly HashSet<string> Redact = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Apple-GS-Token", "X-Apple-I-MD", "X-Apple-I-MD-M", "X-Apple-Identity-Token",
    };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"\n    ─── WIRE → {request.Method} {request.RequestUri}");
        foreach (var h in request.Headers.OrderBy(h => h.Key))
            Console.WriteLine($"        {h.Key}: {Shape(h.Key, string.Join(",", h.Value))}");
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers.OrderBy(h => h.Key))
                Console.WriteLine($"        {h.Key}: {string.Join(",", h.Value)}");
            byte[] reqBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Console.WriteLine($"        [body {reqBody.Length}B]\n{Indent(System.Text.Encoding.UTF8.GetString(reqBody))}");
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        byte[] raw = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        byte[] body = raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B ? Gunzip(raw) : raw;
        Console.WriteLine($"    ─── WIRE ← {(int)response.StatusCode} {response.ReasonPhrase} ({raw.Length}B{(body.Length != raw.Length ? $" gz→{body.Length}B" : "")})");
        Console.WriteLine(Indent(System.Text.Encoding.UTF8.GetString(body)));
        // Re-wrap the consumed body so the caller can still read it.
        var copy = new ByteArrayContent(raw);
        foreach (var h in response.Content.Headers)
            copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        response.Content = copy;
        return response;
    }

    private static string Shape(string key, string value) =>
        !Redact.Contains(key) ? value
        : value.Length <= 12 ? $"<{value.Length}B>"
        : $"<{value.Length}B {value[..6]}…{value[^4..]}>";

    private static string Indent(string s) =>
        "          " + s.Replace("\n", "\n          ");

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gz = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}
