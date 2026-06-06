using Sideport.Core;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>A deterministic <see cref="IAnisetteProvider"/> for tests.</summary>
internal sealed class StubAnisetteProvider : IAnisetteProvider
{
    public int HeaderCalls { get; private set; }

    public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new AnisetteClientInfo(
            "<iMac11,3> <Mac OS X;10.15.6;19G2021> <com.apple.AuthKit/1 (com.apple.dt.Xcode/3594.4.19)>",
            "akd/1.0"));

    public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default)
    {
        HeaderCalls++;
        return Task.FromResult(new AnisetteHeaders(
            MachineId: "TEST-MACHINE-ID",
            OneTimePassword: "TEST-OTP",
            RoutingInfo: "17106176",
            LocalUserId: "TEST-LU",
            ClientTime: DateTimeOffset.UnixEpoch));
    }
}
