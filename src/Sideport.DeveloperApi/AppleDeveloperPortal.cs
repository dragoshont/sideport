using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Claunia.PropertyList;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Packaging;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi;

/// <summary>
/// Clean-room <see cref="IAppleDeveloperPortal"/> over GrandSlam +
/// <c>developerservices2.apple.com</c> (design §6/§8 phases 2-3). Reimplemented
/// from Apple's documented endpoints — never translated from AGPL AltSign
/// source. Authentication (P3) is delegated to <see cref="GrandSlamClient"/>;
/// the developer-services resource methods (P4) run over
/// <see cref="DeveloperServicesClient"/>.
/// </summary>
public sealed class AppleDeveloperPortal : IAppleDeveloperPortal
{
    private readonly GrandSlamClient _grandSlam;
    private readonly DeveloperServicesClient _dev;

    internal AppleDeveloperPortal(GrandSlamClient grandSlam, DeveloperServicesClient dev)
    {
        _grandSlam = grandSlam;
        _dev = dev;
    }

    public Task<AppleLoginResult> AuthenticateAsync(string appleId, string password, CancellationToken ct = default)
        => _grandSlam.AuthenticateAsync(appleId, password, ct);

    public Task SubmitTwoFactorCodeAsync(AppleLoginChallenge challenge, string code, CancellationToken ct = default)
        => _grandSlam.SubmitTwoFactorCodeAsync(challenge, code, ct);

    public async Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default)
    {
        NSDictionary response = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.ListTeams, session, teamId: null, parameters: null, ct);

        var teams = new List<AppleTeam>();
        foreach (NSObject item in PlistCodec.GetArray(response, "teams"))
        {
            if (item is not NSDictionary team)
                continue;
            teams.Add(new AppleTeam(
                PlistCodec.GetString(team, "teamId"),
                PlistCodec.GetStringOrNull(team, "name") ?? "",
                PlistCodec.GetStringOrNull(team, "type") ?? ""));
        }
        return teams;
    }

    public async Task RegisterDeviceAsync(
        AppleSession session, string teamId, string udid, string name, CancellationToken ct = default)
    {
        // Idempotent: listing devices is free-tier safe, and registering an
        // already-registered UDID is an error we want to avoid, so skip when the
        // device is already on the team.
        NSDictionary existing = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.ListDevices, session, teamId, parameters: null, ct);
        foreach (NSObject item in PlistCodec.GetArrayOrEmpty(existing, "devices"))
        {
            if (item is NSDictionary device &&
                PlistCodec.GetStringOrNull(device, "deviceNumber") == udid)
            {
                return;
            }
        }

        await _dev.SendActionAsync(
            DeveloperServicesEndpoints.AddDevice, session, teamId,
            new Dictionary<string, object>
            {
                ["name"] = name,
                ["deviceNumber"] = udid,
                ["DTDK_Platform"] = DeveloperServicesEndpoints.PlatformIos,
            }, ct);
    }

    public async Task<SigningCertificate> EnsureCertificateAsync(
        AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default)
    {
        // Apple's endpoint takes the CSR PEM-encoded; the caller keeps the
        // matching private key and assembles the PKCS#12 from the returned cert.
        string csrPem = PemEncoding.WriteString("CERTIFICATE REQUEST", csrDer);

        NSDictionary response = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.SubmitDevelopmentCsr, session, teamId,
            new Dictionary<string, object>
            {
                ["csrContent"] = csrPem,
                ["machineId"] = Guid.NewGuid().ToString().ToUpperInvariant(),
                ["machineName"] = "Sideport",
            }, ct);

        NSDictionary certRequest = PlistCodec.GetDictionary(response, "certRequest");
        byte[] certificateDer = PlistCodec.GetData(certRequest, "certContent");

        // The certificate itself is authoritative for the serial + expiry.
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateDer);
        return new SigningCertificate(
            certificate.SerialNumber,
            certificateDer,
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }

    public async Task<ProvisioningProfile> EnsureProfileAsync(
        AppleSession session, string teamId, string bundleId, CancellationToken ct = default)
    {
        string appIdId = await EnsureAppIdAsync(session, teamId, bundleId, ct);

        NSDictionary response = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.DownloadTeamProvisioningProfile, session, teamId,
            new Dictionary<string, object>
            {
                ["appIdId"] = appIdId,
                ["DTDK_Platform"] = DeveloperServicesEndpoints.PlatformIos,
            }, ct);

        NSDictionary profileNode = PlistCodec.GetDictionary(response, "provisioningProfile");
        string profileId = PlistCodec.GetStringOrNull(profileNode, "provisioningProfileId") ?? "";
        byte[] mobileProvision = PlistCodec.GetData(profileNode, "encodedProfile");

        ProvisioningProfileInfo info = MobileProvision.Parse(mobileProvision);
        return new ProvisioningProfile(profileId, bundleId, mobileProvision, info.ExpirationDate);
    }

    /// <summary>
    /// Return the Apple-internal App ID id for <paramref name="bundleId"/>,
    /// creating the App ID only when it does not already exist (creation
    /// consumes the free-tier weekly App ID quota, so reuse comes first).
    /// </summary>
    private async Task<string> EnsureAppIdAsync(
        AppleSession session, string teamId, string bundleId, CancellationToken ct)
    {
        NSDictionary list = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.ListAppIds, session, teamId, parameters: null, ct);
        foreach (NSObject item in PlistCodec.GetArrayOrEmpty(list, "appIds"))
        {
            if (item is NSDictionary appId &&
                PlistCodec.GetStringOrNull(appId, "identifier") == bundleId)
            {
                return PlistCodec.GetString(appId, "appIdId");
            }
        }

        NSDictionary added = await _dev.SendActionAsync(
            DeveloperServicesEndpoints.AddAppId, session, teamId,
            new Dictionary<string, object>
            {
                ["name"] = AppIdName(bundleId),
                ["identifier"] = bundleId,
            }, ct);
        NSDictionary created = PlistCodec.GetDictionary(added, "appId");
        return PlistCodec.GetString(created, "appIdId");
    }

    /// <summary>
    /// A display name Apple accepts for an App ID: alphanumerics and spaces only
    /// (dots and other punctuation are rejected).
    /// </summary>
    private static string AppIdName(string bundleId)
    {
        char[] chars = bundleId.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        string cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Sideport App" : $"Sideport {cleaned}";
    }
}

