using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
        // Sideport can reuse only a certificate whose matching private key is in
        // its persisted identity store. Finding some other development
        // certificate here is not permission to revoke it: it may belong to Xcode,
        // AltStore, or another Sideport deployment. Replacement needs a separate,
        // exact-impact confirmation flow, so this low-level ensure path fails
        // closed and leaves Apple's inventory untouched.
        DevelopmentCertificateInventory inventory =
            await ReadDevelopmentCertificateInventoryAsync(session, teamId, ct);
        if (inventory.Count > 0)
        {
            SigningCertificate? recoverable = FindCertificateForCsr(inventory.Downloadable, csrDer);
            if (recoverable is not null)
                return recoverable;

            throw new SigningCertificateReplacementRequiredException(inventory.Count);
        }

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

        // Apple returns the new certificate one of two ways (the reference signer
        // parses both): inline as certRequest.certContent (a <data> DER), or — the
        // shape seen live — as metadata only (name + serialNumber), with the DER
        // served from the JSON certificates list. Prefer the inline DER; otherwise
        // fetch it from services/v1/certificates, matched by serial number.
        byte[] certificateDer = PlistCodec.TryGetData(certRequest, "certContent", out byte[] inlineDer)
            ? inlineDer
            : await FetchDevelopmentCertificateDerAsync(
                session, teamId,
                PlistCodec.GetStringOrNull(certRequest, "serialNumber")
                    ?? PlistCodec.GetStringOrNull(certRequest, "serialNum"),
                ct);

        // The certificate itself is authoritative for the serial + expiry.
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateDer);
        return new SigningCertificate(
            certificate.SerialNumber,
            certificateDer,
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }

    public async Task<IReadOnlyList<AppleDevelopmentCertificate>> ListDevelopmentCertificatesAsync(
        AppleSession session,
        string teamId,
        CancellationToken ct = default)
    {
        DevelopmentCertificateInventory inventory =
            await ReadDevelopmentCertificateInventoryAsync(session, teamId, ct);
        return inventory.Certificates;
    }

    public async Task RevokeDevelopmentCertificateAsync(
        AppleSession session,
        string teamId,
        string certificateId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateId);
        await _dev.SendServicesRequestAsync(
            $"certificates/{Uri.EscapeDataString(certificateId)}",
            "DELETE",
            session,
            teamId,
            query: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetch the DER of the just-issued iOS development certificate from the JSON
    /// services list (<c>GET services/v1/certificates</c>). Apple's
    /// submitDevelopmentCSR response can carry only certificate metadata, so the
    /// content is read from <c>data[].attributes.certificateContent</c> (base64),
    /// matched by serial number when known, else the sole development certificate
    /// (a fresh mint leaves exactly one content-bearing certificate on the team).
    /// </summary>
    private async Task<byte[]> FetchDevelopmentCertificateDerAsync(
        AppleSession session, string teamId, string? serialNumber, CancellationToken ct)
    {
        JsonElement list = await _dev.SendServicesRequestAsync(
            "certificates", "GET", session, teamId,
            new Dictionary<string, string> { ["filter[certificateType]"] = "IOS_DEVELOPMENT" }, ct);

        if (list.ValueKind != JsonValueKind.Object ||
            !list.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() == 0)
        {
            throw new DeveloperServicesException(
                "submitDevelopmentCSR returned no inline certificate and the certificates list is empty");
        }

        // Prefer an exact serial match; otherwise keep the sole content-bearing
        // entry created by this fresh mint.
        string? chosen = null;
        foreach (JsonElement cert in data.EnumerateArray())
        {
            if (!cert.TryGetProperty("attributes", out JsonElement attrs) ||
                !attrs.TryGetProperty("certificateContent", out JsonElement content) ||
                content.GetString() is not { Length: > 0 } base64)
            {
                continue;
            }

            chosen = base64;
            if (serialNumber is not null &&
                attrs.TryGetProperty("serialNumber", out JsonElement serial) &&
                serial.GetString() == serialNumber)
            {
                break;
            }
        }

        if (chosen is null)
        {
            throw new DeveloperServicesException(
                "no downloadable iOS development certificate found after submitDevelopmentCSR");
        }

        return Convert.FromBase64String(chosen);
    }

    private async Task<DevelopmentCertificateInventory> ReadDevelopmentCertificateInventoryAsync(
        AppleSession session, string teamId, CancellationToken ct)
    {
        JsonElement list = await _dev.SendServicesRequestAsync(
            "certificates", "GET", session, teamId,
            new Dictionary<string, string> { ["filter[certificateType]"] = "IOS_DEVELOPMENT" }, ct);

        if (list.ValueKind != JsonValueKind.Object ||
            !list.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw new DeveloperServicesException(
                "Apple returned an invalid development-certificate inventory");
        }

        var certificates = new List<AppleDevelopmentCertificate>();
        var downloadable = new List<SigningCertificate>();
        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("id", out JsonElement idNode) ||
                idNode.GetString() is not { Length: > 0 } id ||
                !entry.TryGetProperty("attributes", out JsonElement attributes) ||
                attributes.ValueKind != JsonValueKind.Object)
                throw new DeveloperServicesException(
                    "Apple returned an invalid development-certificate inventory entry");

            string serial = attributes.TryGetProperty("serialNumber", out JsonElement serialNode)
                ? serialNode.GetString() ?? string.Empty
                : string.Empty;
            DateTimeOffset? expiresAt = null;
            if (attributes.TryGetProperty("expirationDate", out JsonElement expiryNode) &&
                expiryNode.GetString() is { Length: > 0 } expiryText &&
                DateTimeOffset.TryParse(expiryText, out DateTimeOffset parsedExpiry))
                expiresAt = parsedExpiry.ToUniversalTime();

            if (attributes.TryGetProperty("certificateContent", out JsonElement content) &&
                content.GetString() is { Length: > 0 } encoded)
            {
                try
                {
                    byte[] certificateDer = Convert.FromBase64String(encoded);
                    using X509Certificate2 certificate =
                        X509CertificateLoader.LoadCertificate(certificateDer);
                    serial = certificate.SerialNumber;
                    expiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                    downloadable.Add(new SigningCertificate(
                        serial,
                        certificateDer,
                        expiresAt.Value));
                }
                catch (Exception error) when (error is FormatException or CryptographicException)
                {
                    throw new DeveloperServicesException(
                        "Apple returned an invalid downloadable development certificate", error);
                }
            }

            certificates.Add(new AppleDevelopmentCertificate(id, serial, expiresAt));
        }

        return new DevelopmentCertificateInventory(certificates, downloadable);
    }

    private static SigningCertificate? FindCertificateForCsr(
        IReadOnlyList<SigningCertificate> certificates,
        byte[] csrDer)
    {
        string csrPem = PemEncoding.WriteString("CERTIFICATE REQUEST", csrDer);
        CertificateRequest request = CertificateRequest.LoadSigningRequestPem(
            csrPem,
            HashAlgorithmName.SHA256,
            signerSignaturePadding: RSASignaturePadding.Pkcs1);
        byte[] requestedPublicKey = request.PublicKey.ExportSubjectPublicKeyInfo();

        foreach (SigningCertificate candidate in certificates)
        {
            using X509Certificate2 certificate =
                X509CertificateLoader.LoadCertificate(candidate.CertificateDer);
            byte[] candidatePublicKey = certificate.PublicKey.ExportSubjectPublicKeyInfo();
            if (requestedPublicKey.Length == candidatePublicKey.Length &&
                CryptographicOperations.FixedTimeEquals(requestedPublicKey, candidatePublicKey))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record DevelopmentCertificateInventory(
        IReadOnlyList<AppleDevelopmentCertificate> Certificates,
        IReadOnlyList<SigningCertificate> Downloadable)
    {
        public int Count => Certificates.Count;
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
