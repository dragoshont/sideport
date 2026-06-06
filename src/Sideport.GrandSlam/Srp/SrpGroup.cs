using System.Numerics;
using Sideport.GrandSlam.Numerics;

namespace Sideport.GrandSlam.Srp;

/// <summary>
/// The RFC 5054 Appendix A 2048-bit SRP group (modulus <c>N</c>, generator
/// <c>g = 2</c>) — the group Apple GrandSlam negotiates with.
/// </summary>
public static class SrpGroup
{
    // RFC 5054, Appendix A — 2048-bit group N (hex, big-endian). g = 2.
    private const string N2048Hex =
        "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050" +
        "A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50" +
        "E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B8" +
        "55F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773B" +
        "CA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748" +
        "544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6" +
        "AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB6" +
        "94B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73";

    /// <summary>The 2048-bit safe-prime modulus <c>N</c>.</summary>
    public static readonly BigInteger N =
        BigEndianBytes.ToBigInteger(Convert.FromHexString(N2048Hex));

    /// <summary>The generator <c>g = 2</c>.</summary>
    public static readonly BigInteger G = new(2);

    /// <summary>Width of <c>N</c> in bytes (256 = 2048 bits) — the SRP <c>PAD</c> width.</summary>
    public const int Width = 256;
}
