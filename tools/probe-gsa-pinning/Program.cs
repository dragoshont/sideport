// Live one-shot probe (not a unit test): does AppleCaPinning.Validate accept the
// REAL gsa.apple.com server certificate? Read-only TLS handshake, no auth, no
// developer-API calls, no free-tier impact. Run: dotnet run --project tools/probe-gsa-pinning
using System.Net.Http;
using Sideport.DeveloperApi.GrandSlam;

bool pinnedAccepted = false;
bool publicTrustResult;

var pinned = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
    {
        pinnedAccepted = AppleCaPinning.Validate(cert, chain);
        return pinnedAccepted;
    },
};

try
{
    using var client = new HttpClient(pinned);
    using var resp = await client.GetAsync("https://gsa.apple.com/grandslam/GsService2");
    Console.WriteLine($"pinned handshake OK, HTTP {(int)resp.StatusCode}");
}
catch (Exception ex)
{
    Console.WriteLine($"pinned handshake threw: {ex.GetType().Name}: {ex.Message}");
}
Console.WriteLine($"AppleCaPinning.Validate accepted real GSA cert: {pinnedAccepted}");

// Contrast: default public-trust validation should FAIL (private intermediate).
try
{
    using var def = new HttpClient();
    using var resp = await def.GetAsync("https://gsa.apple.com/grandslam/GsService2");
    publicTrustResult = true;
    Console.WriteLine($"public-trust handshake OK, HTTP {(int)resp.StatusCode}");
}
catch (Exception ex)
{
    publicTrustResult = false;
    Console.WriteLine($"public-trust handshake failed (expected): {ex.GetType().Name}");
}

Console.WriteLine();
Console.WriteLine($"RESULT: pinned={pinnedAccepted} publicTrust={publicTrustResult}");
return pinnedAccepted ? 0 : 1;
