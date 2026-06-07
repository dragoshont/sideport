using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Sideport.Core;

namespace Sideport.DeveloperApi;

/// <summary>
/// Default <see cref="IAnisetteProvider"/>: talks to a Dadoum
/// <c>anisette-v3-server</c> sidecar (design §5). Pin the image by digest and
/// persist its ADI volume — losing it burns an Apple trusted-device slot and
/// forces re-2FA.
///
/// Headers are fetched from the flat <c>GET /</c> ("v1") endpoint: it returns
/// the full ADI-provisioned set in one shot, INCLUDING <c>X-Mme-Device-Id</c>
/// and <c>X-MMe-Client-Info</c> (the machine identity Apple's trust is bound to).
/// Reusing those is what lets Sideport inherit an existing anisette/AltServer
/// trust and authenticate without re-triggering 2FA. The newer
/// <c>/v3/get_headers</c> endpoint requires a separate ADI provisioning step
/// that not every deployment performs, so the universally-provisioned flat
/// endpoint is preferred.
/// </summary>
public sealed class ContainerAnisetteProvider(HttpClient http) : IAnisetteProvider
{
    public async Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default)
    {
        // /v3/client_info is static metadata (no ADI provisioning needed), so it
        // is reliable regardless of the get-headers contract in use.
        var dto = await http.GetFromJsonAsync<ClientInfoDto>("v3/client_info", ct)
                  ?? throw new InvalidOperationException("anisette: empty /v3/client_info");
        return new AnisetteClientInfo(dto.client_info ?? "", dto.user_agent ?? "");
    }

    public async Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default)
    {
        // The flat root endpoint returns the trusted header set as a JSON object
        // of dashed string keys (e.g. "X-Apple-I-MD"). Read it defensively into a
        // map so server formatting differences (string vs number RINFO) are fine.
        var map = await http.GetFromJsonAsync<Dictionary<string, JsonElement>>("", ct)
                  ?? throw new InvalidOperationException("anisette: empty GET /");

        string Read(params string[] keys)
        {
            foreach (string key in keys)
                if (map.TryGetValue(key, out JsonElement value))
                    return value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? ""
                        : value.ToString();
            return "";
        }

        string oneTimePassword = Read("X-Apple-I-MD");
        if (string.IsNullOrEmpty(oneTimePassword))
            throw new InvalidOperationException(
                "anisette: GET / returned no X-Apple-I-MD — the ADI is likely not " +
                "provisioned. Persist + seed the anisette ADI volume (see design §5/S6).");

        // The one-time password is bound to the EXACT instant the anisette minted
        // it; that instant is reported as X-Apple-I-Client-Time. The developer-
        // services endpoints validate the OTP against the client-time we send, so
        // we MUST echo the anisette's value — using the local clock here yields a
        // "session expired" (1100) even though the OTP is fresh. Fall back to the
        // local clock only when the server omits it.
        string clientTimeRaw = Read("X-Apple-I-Client-Time");
        DateTimeOffset clientTime = DateTimeOffset.TryParse(
            clientTimeRaw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return new AnisetteHeaders(
            MachineId: Read("X-Apple-I-MD-M"),
            OneTimePassword: oneTimePassword,
            RoutingInfo: Read("X-Apple-I-MD-RINFO"),
            LocalUserId: Read("X-Apple-I-MD-LU"),
            ClientTime: clientTime,
            DeviceId: Read("X-Mme-Device-Id", "X-MMe-Device-Id"),
            ClientInfo: Read("X-MMe-Client-Info", "X-Mme-Client-Info"));
    }

    private sealed record ClientInfoDto(string? client_info, string? user_agent);
}
