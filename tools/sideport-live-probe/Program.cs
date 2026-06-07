using Claunia.PropertyList;
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
var grandSlam = new GrandSlamClient(gsaHttp, anisette, options, NullLogger<GrandSlamClient>.Instance);

// developerservices2 client (normal public-trust TLS).
var devHttp = new HttpClient();
var dev = new DeveloperServicesClient(devHttp, anisette, options, NullLogger<DeveloperServicesClient>.Instance);

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
