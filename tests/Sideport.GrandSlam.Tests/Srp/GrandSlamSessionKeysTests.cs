using Sideport.GrandSlam.Srp;
using Sideport.GrandSlam.Tests.Vectors;

namespace Sideport.GrandSlam.Tests.Srp;

public class GrandSlamSessionKeysTests
{
    [Fact]
    public void Derive_MatchesGoldenVectorSubkeys()
    {
        var keys = new GrandSlamSessionKeys(AppleSrpVector.SessionKey);

        Assert.Equal(AppleSrpVector.SessionKey, keys.SessionKey);
        Assert.Equal(AppleSrpVector.ExtraDataKey, keys.ExtraDataKey);
        Assert.Equal(AppleSrpVector.ExtraDataIv, keys.ExtraDataIv);
        Assert.Equal(AppleSrpVector.HmacKey, keys.HmacKey);
    }

    [Fact]
    public void Derive_ProducesExpectedKeyAndIvSizes()
    {
        var keys = new GrandSlamSessionKeys(AppleSrpVector.SessionKey);

        Assert.Equal(32, keys.ExtraDataKey.Length);
        Assert.Equal(16, keys.ExtraDataIv.Length);
        Assert.Equal(32, keys.HmacKey.Length);
    }

    [Fact]
    public void Constructor_EmptySessionKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => new GrandSlamSessionKeys([]));
    }
}
