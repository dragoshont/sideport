using Sideport.GrandSlam;
using Sideport.GrandSlam.Tests.Vectors;

namespace Sideport.GrandSlam.Tests;

/// <summary>
/// Tests for the GrandSlam crypto primitives. The SRP-6a keystone itself lives
/// in <see cref="Srp.AppleSrpClientTests"/>; here we pin the s2k password-key
/// derivation against the same libgsa golden vector.
/// </summary>
public class GrandSlamCryptoTests
{
    [Fact]
    public void Pad_LeftPadsToLength()
    {
        byte[] padded = GrandSlamCrypto.Pad([0xAB, 0xCD], 4);
        Assert.Equal([0x00, 0x00, 0xAB, 0xCD], padded);
    }

    [Fact]
    public void Pad_RejectsOverlongValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GrandSlamCrypto.Pad([0x01, 0x02, 0x03], 2));
    }

    [Fact]
    public void DerivePasswordKey_IsDeterministicAnd32Bytes()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] a = GrandSlamCrypto.DerivePasswordKey("hunter2", salt, 1000);
        byte[] b = GrandSlamCrypto.DerivePasswordKey("hunter2", salt, 1000);

        Assert.Equal(32, a.Length);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DerivePasswordKey_HexExpandDiffersFromRaw()
    {
        byte[] salt = [9, 8, 7, 6, 5, 4, 3, 2];
        byte[] raw = GrandSlamCrypto.DerivePasswordKey("pw", salt, 500, hexExpand: false);
        byte[] fo  = GrandSlamCrypto.DerivePasswordKey("pw", salt, 500, hexExpand: true);

        Assert.NotEqual(raw, fo);
    }

    [Fact]
    public void DerivePasswordKey_MatchesGoldenVector()
    {
        byte[] key = GrandSlamCrypto.DerivePasswordKey(
            AppleSrpVector.Password, AppleSrpVector.Salt, AppleSrpVector.Iterations);

        Assert.Equal(AppleSrpVector.PasswordKey, key);
    }
}
