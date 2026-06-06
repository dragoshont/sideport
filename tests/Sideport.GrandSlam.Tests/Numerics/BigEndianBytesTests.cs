using System.Numerics;
using Sideport.GrandSlam.Numerics;

namespace Sideport.GrandSlam.Tests.Numerics;

public class BigEndianBytesTests
{
    [Fact]
    public void ToBigInteger_EmptySpan_IsZero()
    {
        Assert.Equal(BigInteger.Zero, BigEndianBytes.ToBigInteger([]));
    }

    [Fact]
    public void ToBigInteger_ParsesUnsignedBigEndian()
    {
        // 0x0102 = 258, high byte first.
        Assert.Equal(new BigInteger(258), BigEndianBytes.ToBigInteger([0x01, 0x02]));
    }

    [Fact]
    public void ToBigInteger_HighBitByteIsPositive()
    {
        // 0xFF must parse as 255, not as a negative two's-complement value.
        Assert.Equal(new BigInteger(255), BigEndianBytes.ToBigInteger([0xFF]));
    }

    [Fact]
    public void ToMinimalBytes_Zero_IsSingleZeroByte()
    {
        Assert.Equal([0x00], BigEndianBytes.ToMinimalBytes(BigInteger.Zero));
    }

    [Fact]
    public void ToMinimalBytes_StripsLeadingZeros()
    {
        Assert.Equal([0x01], BigEndianBytes.ToMinimalBytes(new BigInteger(1)));
    }

    [Fact]
    public void ToMinimalBytes_HighBitValue_HasNoSignByte()
    {
        // 0x80 must render as a single byte, not [0x00, 0x80] (the OpenSSL
        // BN_bn2bin contract that the SRP recipe depends on).
        Assert.Equal([0x80], BigEndianBytes.ToMinimalBytes(new BigInteger(0x80)));
    }

    [Fact]
    public void ToMinimalBytes_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BigEndianBytes.ToMinimalBytes(BigInteger.MinusOne));
    }

    [Fact]
    public void ToFixedBytes_LeftPadsWithZeros()
    {
        Assert.Equal([0x00, 0x00, 0x01, 0x02], BigEndianBytes.ToFixedBytes(new BigInteger(258), 4));
    }

    [Fact]
    public void ToFixedBytes_Zero_IsAllZeros()
    {
        Assert.Equal([0x00, 0x00, 0x00], BigEndianBytes.ToFixedBytes(BigInteger.Zero, 3));
    }

    [Fact]
    public void ToFixedBytes_HighBitValue_NotSignExtended()
    {
        Assert.Equal([0x00, 0x80], BigEndianBytes.ToFixedBytes(new BigInteger(0x80), 2));
    }

    [Fact]
    public void ToFixedBytes_ValueWiderThanWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BigEndianBytes.ToFixedBytes(new BigInteger(0x0102), 1));
    }

    [Fact]
    public void ToFixedBytes_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BigEndianBytes.ToFixedBytes(BigInteger.MinusOne, 4));
    }

    [Fact]
    public void RoundTrip_FixedWidth_PreservesValue()
    {
        byte[] fixed256 = AppleSrpVectorBytes();
        BigInteger value = BigEndianBytes.ToBigInteger(fixed256);
        byte[] back = BigEndianBytes.ToFixedBytes(value, 256);
        Assert.Equal(fixed256, back);
    }

    private static byte[] AppleSrpVectorBytes()
    {
        // A 256-byte value whose top byte has the high bit set — the exact shape
        // of an SRP public value, to exercise the no-sign-byte path at width.
        byte[] b = new byte[256];
        b[0] = 0x80;
        for (int i = 1; i < b.Length; i++)
            b[i] = (byte)i;
        return b;
    }
}
