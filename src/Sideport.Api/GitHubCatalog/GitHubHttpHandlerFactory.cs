using System.Net;
using System.Net.Sockets;

namespace Sideport.Api.GitHubCatalog;

/// <summary>
/// Creates the production GitHub handler. Redirects stay visible to the service and
/// the validated DNS result is the address actually connected, closing DNS-rebinding gaps.
/// </summary>
public static class GitHubHttpHandlerFactory
{
    public static SocketsHttpHandler Create(
        GitHubCatalogOptions options,
        IGitHubDnsResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        IGitHubDnsResolver dns = resolver ?? new SystemGitHubDnsResolver();
        GitHubNetworkSafety.ValidateAllowedHosts(options.AllowedDownloadHosts);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        handler.ConnectCallback = async (context, ct) =>
        {
            IReadOnlyList<IPAddress> addresses = await GitHubNetworkSafety.ResolveAllowedPublicAsync(
                context.DnsEndPoint.Host,
                context.DnsEndPoint.Port,
                options.AllowedDownloadHosts,
                dns,
                ct).ConfigureAwait(false);
            foreach (IPAddress address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    socket.Dispose();
                }
            }
            throw new HttpRequestException("The GitHub connection is unavailable.");
        };
        return handler;
    }
}

internal static class GitHubNetworkSafety
{
    private static readonly HashSet<string> KnownGitHubHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    public static void ValidateAllowedHosts(IReadOnlyList<string> allowedHosts)
    {
        var configured = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);
        if (!configured.Contains("api.github.com") ||
            configured.Count == 0 ||
            configured.Any(host => !KnownGitHubHosts.Contains(host)))
        {
            throw new InvalidOperationException("The GitHub host allowlist may contain only supported exact GitHub hosts and must include api.github.com.");
        }
    }

    public static async ValueTask<IReadOnlyList<IPAddress>> ResolveAllowedPublicAsync(
        string host,
        int port,
        IReadOnlyCollection<string> allowedHosts,
        IGitHubDnsResolver resolver,
        CancellationToken ct)
    {
        if (port != 443 || !allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw Rejected();
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw Rejected();
        }
        if (addresses.Count == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw Rejected();
        return addresses;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;
        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicIpv4(bytes);
        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            (bytes[0] & 0xFE) == 0xFC ||
            bytes[0] == 0xFF)
        {
            return false;
        }

        // Only current global-unicast space is eligible. Explicitly exclude
        // transition/special ranges that can route an embedded private IPv4
        // destination (NAT64 WKPs are outside 2000::/3; 6to4/Teredo/ISATAP are
        // handled below), plus documentation and non-routable allocations.
        if ((bytes[0] & 0xE0) != 0x20 ||
            (bytes[0] == 0x20 && bytes[1] == 0x02) ||
            (bytes[0] == 0x3F && bytes[1] == 0xFE) ||
            HasPrefix(bytes, [0x20, 0x01, 0x00, 0x00], 32) ||
            HasPrefix(bytes, [0x20, 0x01, 0x00, 0x02, 0x00, 0x00], 48) ||
            HasPrefix(bytes, [0x20, 0x01, 0x0D, 0xB8], 32) ||
            IsOrchid(bytes) ||
            IsIsatapWithPrivateIpv4(bytes))
        {
            return false;
        }

        return true;
    }

    private static bool IsPublicIpv4(ReadOnlySpan<byte> bytes)
    {
        int first = bytes[0];
        int second = bytes[1];
        return first != 0 && first != 10 && first != 127 &&
               !(first == 100 && second is >= 64 and <= 127) &&
               !(first == 169 && second == 254) &&
               !(first == 172 && second is >= 16 and <= 31) &&
               !(first == 192 && second == 0 && bytes[2] == 0) &&
               !(first == 192 && second == 0 && bytes[2] == 2) &&
               !(first == 192 && second == 88 && bytes[2] == 99) &&
               !(first == 192 && second == 168) &&
               !(first == 198 && second is 18 or 19) &&
               !(first == 198 && second == 51 && bytes[2] == 100) &&
               !(first == 203 && second == 0 && bytes[2] == 113) &&
               first < 224;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> address, ReadOnlySpan<byte> prefix, int bits)
    {
        int wholeBytes = bits / 8;
        int remainder = bits % 8;
        if (!address[..wholeBytes].SequenceEqual(prefix[..wholeBytes]))
            return false;
        if (remainder == 0)
            return true;
        int mask = 0xFF << (8 - remainder);
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }

    private static bool IsOrchid(ReadOnlySpan<byte> bytes) =>
        HasPrefix(bytes, [0x20, 0x01, 0x00, 0x10], 28) ||
        HasPrefix(bytes, [0x20, 0x01, 0x00, 0x20], 28);

    private static bool IsIsatapWithPrivateIpv4(ReadOnlySpan<byte> bytes)
    {
        bool isIsatap = bytes[8] is 0x00 or 0x02 &&
                        bytes[9] == 0x00 &&
                        bytes[10] == 0x5E &&
                        bytes[11] == 0xFE;
        return isIsatap && !IsPublicIpv4(bytes.Slice(12, 4));
    }

    private static GitHubCatalogException Rejected() =>
        new("github-redirect-rejected", "The GitHub network destination was rejected.");
}
