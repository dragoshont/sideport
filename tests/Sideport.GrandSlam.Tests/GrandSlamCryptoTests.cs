using Sideport.GrandSlam;

namespace Sideport.GrandSlam.Tests;

/// <summary>
/// Scaffold tests for the clean-room GrandSlam crypto. The keystone work
/// (design §8 phase 2) is to assert the managed SRP-6a output byte-for-byte
/// against the libgsa golden vectors — those vectors get imported here as an
/// oracle (libgsa is a test-only dependency, never shipped at runtime).
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

    [Fact(Skip = "Phase 2 keystone: assert managed SRP-6a vs libgsa golden vectors.")]
    public void Srp_MatchesLibgsaOracleVectors()
    {
        // Import tests/vectors/apple_srp_vector.h equivalents (k,u,x,S,K,M1,M2)
        // and assert the managed implementation reproduces them exactly.
    }
}
