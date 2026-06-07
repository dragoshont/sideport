using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// Validates the TLS server certificate of <c>gsa.apple.com</c> by pinning
/// Apple's private CA.
///
/// GrandSlam is served from Apple's <em>private</em> PKI: the chain is
/// <c>gsa.apple.com → "Apple Server Authentication CA" → "Apple Root CA"</c>,
/// and the intermediate is not in the public/OS trust store, so ordinary
/// public-trust validation fails outright (this is why the whole ecosystem —
/// pypush, AltServer, SideStore — disables verification for GSA). Rather than
/// disable verification, Sideport pins Apple's actual root and validates the
/// chain against it as a custom trust anchor. This authenticates the server
/// (defeats a transparent MITM) on top of the SRP M2 check, without trusting an
/// arbitrary cert.
/// </summary>
internal static class AppleCaPinning
{
    private const string ResourceName = "Sideport.DeveloperApi.Resources.AppleRootCA-G1.pem";

    private static readonly Lazy<X509Certificate2[]> PinnedRoots = new(LoadPinnedRoots);

    /// <summary>
    /// The pinned Apple root certificate(s), as fresh copies so callers can
    /// dispose them without affecting the cached anchors.
    /// </summary>
    public static X509Certificate2Collection Roots =>
        [.. PinnedRoots.Value.Select(c => X509CertificateLoader.LoadCertificate(c.RawData))];

    /// <summary>
    /// A <see cref="System.Net.Http.HttpClientHandler.ServerCertificateCustomValidationCallback"/>
    /// that accepts a leaf only if it builds a valid chain to a pinned Apple root.
    /// </summary>
    public static bool Validate(X509Certificate2? leaf, X509Chain? builtChain)
    {
        if (leaf is null)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.AddRange(PinnedRoots.Value);

        // Carry over any intermediates the server supplied so the chain can build
        // even though the intermediate isn't publicly trusted.
        if (builtChain is not null)
        {
            foreach (X509ChainElement element in builtChain.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        if (!chain.Build(leaf))
            return false;

        // The chain must actually terminate at one of our pinned roots, not merely
        // be self-consistent.
        X509Certificate2 root = chain.ChainElements[^1].Certificate;
        foreach (X509Certificate2 pinned in PinnedRoots.Value)
        {
            if (root.RawData.AsSpan().SequenceEqual(pinned.RawData))
                return true;
        }
        return false;
    }

    private static X509Certificate2[] LoadPinnedRoots()
    {
        using Stream stream = typeof(AppleCaPinning).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded Apple root CA '{ResourceName}' not found");
        using var reader = new StreamReader(stream);
        string pem = reader.ReadToEnd();
        return [X509Certificate2.CreateFromPem(pem)];
    }
}
