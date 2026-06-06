using System.Numerics;

namespace Sideport.GrandSlam.Numerics;

/// <summary>
/// Unsigned big-endian conversions between byte spans and <see cref="BigInteger"/>.
///
/// SRP and the GrandSlam protocol speak unsigned big-endian throughout, while
/// <see cref="BigInteger"/> is internally little-endian two's-complement. Every
/// crossing of that boundary goes through this one audited type so the
/// endianness/sign handling lives in exactly one place (see the keystone risk
/// in the implementation plan, §P2).
/// </summary>
public static class BigEndianBytes
{
    /// <summary>
    /// Parse an unsigned big-endian magnitude into a non-negative
    /// <see cref="BigInteger"/>. An empty span parses to zero.
    /// </summary>
    public static BigInteger ToBigInteger(ReadOnlySpan<byte> bigEndian) =>
        bigEndian.IsEmpty
            ? BigInteger.Zero
            : new BigInteger(bigEndian, isUnsigned: true, isBigEndian: true);

    /// <summary>
    /// Minimal unsigned big-endian bytes — no sign byte, no leading zeros —
    /// matching OpenSSL <c>BN_bn2bin</c> and pysrp's <c>long_to_bytes</c>.
    /// Zero renders as a single <c>0x00</c> byte.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static byte[] ToMinimalBytes(BigInteger value)
    {
        if (value.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");
        if (value.IsZero)
            return [0x00];
        return value.ToByteArray(isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Fixed-width unsigned big-endian bytes, left-padded with zeros, matching
    /// OpenSSL <c>BN_bn2binpad</c>. Used for the SRP <c>PAD(...)</c> inputs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is negative, or its magnitude does not fit in
    /// <paramref name="width"/> bytes.
    /// </exception>
    public static byte[] ToFixedBytes(BigInteger value, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        if (value.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative");

        byte[] minimal = value.IsZero
            ? []
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);

        if (minimal.Length > width)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"value needs {minimal.Length} bytes, wider than the requested {width}");

        byte[] padded = new byte[width];
        minimal.CopyTo(padded.AsSpan(width - minimal.Length));
        return padded;
    }
}
