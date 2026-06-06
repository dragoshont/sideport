using System.Net.Http.Json;
using Sideport.Core;

namespace Sideport.DeveloperApi;

/// <summary>
/// Default <see cref="IAnisetteProvider"/>: talks the anisette v3 HTTP contract
/// to a Dadoum <c>anisette-v3-server</c> sidecar (design §5). Pin the image by
/// digest and persist its ADI volume — losing it burns an Apple trusted-device
/// slot and forces re-2FA.
/// </summary>
public sealed class ContainerAnisetteProvider(HttpClient http) : IAnisetteProvider
{
    public async Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default)
    {
        var dto = await http.GetFromJsonAsync<ClientInfoDto>("v3/client_info", ct)
                  ?? throw new InvalidOperationException("anisette: empty /v3/client_info");
        return new AnisetteClientInfo(dto.client_info ?? "", dto.user_agent ?? "");
    }

    public async Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default)
    {
        var dto = await http.GetFromJsonAsync<HeadersDto>("v3/get_headers", ct)
                  ?? throw new InvalidOperationException("anisette: empty /v3/get_headers");
        return new AnisetteHeaders(
            dto.X_Apple_I_MD_M ?? "",
            dto.X_Apple_I_MD ?? "",
            dto.X_Apple_I_MD_RINFO ?? "",
            dto.X_Apple_I_MD_LU ?? "",
            DateTimeOffset.UtcNow);
    }

    private sealed record ClientInfoDto(string? client_info, string? user_agent);

    private sealed record HeadersDto(
        string? X_Apple_I_MD_M,
        string? X_Apple_I_MD,
        string? X_Apple_I_MD_RINFO,
        string? X_Apple_I_MD_LU);
}
