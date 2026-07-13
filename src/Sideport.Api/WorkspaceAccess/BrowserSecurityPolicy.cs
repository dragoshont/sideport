namespace Sideport.Api.WorkspaceAccess;

internal static class BrowserSecurityPolicy
{
    public static Uri ParsePublicOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw InvalidPublicOrigin();

        string candidate = value.Trim();
        if (candidate.Any(char.IsControl) ||
            candidate.Contains('\\') ||
            candidate.Contains('?') ||
            candidate.Contains('#') ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw InvalidPublicOrigin();
        }

        int authorityMarker = candidate.IndexOf("://", StringComparison.Ordinal);
        if (authorityMarker <= 0)
            throw InvalidPublicOrigin();
        int pathStart = candidate.IndexOf('/', authorityMarker + 3);
        if (pathStart >= 0 && !string.Equals(candidate[pathStart..], "/", StringComparison.Ordinal))
            throw InvalidPublicOrigin();

        bool https = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        bool loopbackDevelopmentHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
            IsLoopbackHost(uri);
        if (!https && !loopbackDevelopmentHttp)
            throw InvalidPublicOrigin();

        return new Uri($"{uri.GetLeftPart(UriPartial.Authority)}/", UriKind.Absolute);
    }

    public static bool TryNormalizeLocalReturnPath(string? value, out string path)
    {
        path = "/";
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string candidate = value.Trim();
        if (candidate[0] != '/' ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.Contains('\\') ||
            candidate.Contains('#') ||
            ContainsControl(candidate))
        {
            return false;
        }

        string decoded = candidate;
        for (int pass = 0; pass < 2; pass++)
        {
            try
            {
                decoded = Uri.UnescapeDataString(decoded);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (decoded.Length == 0 || decoded[0] != '/' ||
                decoded.StartsWith("//", StringComparison.Ordinal) ||
                decoded.Contains('\\') ||
                decoded.Contains('#') ||
                ContainsControl(decoded))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(candidate, UriKind.Relative, out _))
            return false;

        path = candidate;
        return true;
    }

    private static bool ContainsControl(string value) => value.Any(char.IsControl);

    private static bool IsLoopbackHost(Uri uri)
    {
        if (string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return System.Net.IPAddress.TryParse(uri.DnsSafeHost, out System.Net.IPAddress? address) &&
            System.Net.IPAddress.IsLoopback(address);
    }

    private static InvalidOperationException InvalidPublicOrigin() =>
        new(
            "Sideport:PublicOrigin must be a fixed HTTPS origin with no credentials, path, query, or fragment. " +
            "HTTP is allowed only for an explicit loopback development origin.");
}
