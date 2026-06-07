using Claunia.PropertyList;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi.Tests.Plist;

public class PlistCodecTests
{
    [Fact]
    public void RoundTrip_NestedDictionary_PreservesValues()
    {
        var graph = new Dictionary<string, object>
        {
            ["str"] = "hello",
            ["num"] = 42,
            ["flag"] = true,
            ["data"] = new byte[] { 1, 2, 3, 4 },
            ["list"] = new[] { "a", "b" },
            ["nested"] = new Dictionary<string, object> { ["inner"] = "value" },
        };

        byte[] xml = PlistCodec.ToXmlBytes(graph);
        NSDictionary parsed = PlistCodec.ParseDictionary(xml);

        Assert.Equal("hello", PlistCodec.GetString(parsed, "str"));
        Assert.Equal(42, PlistCodec.GetLong(parsed, "num"));
        Assert.Equal([1, 2, 3, 4], PlistCodec.GetData(parsed, "data"));
        Assert.Equal("value", PlistCodec.GetString(PlistCodec.GetDictionary(parsed, "nested"), "inner"));
    }

    [Fact]
    public void ParseDictionary_BarePlistWithoutXmlPrologue_Succeeds()
    {
        // A <plist> body lacking the <?xml?> + DOCTYPE prologue — the SPD-blob
        // shape. The codec must still parse it (directly or via prologue retry).
        byte[] bare = "<plist version=\"1.0\"><dict><key>adsid</key><string>abc</string></dict></plist>"u8.ToArray();

        NSDictionary parsed = PlistCodec.ParseDictionary(bare);
        Assert.Equal("abc", PlistCodec.GetString(parsed, "adsid"));
    }

    [Fact]
    public void ParseDictionary_BareDictFragment_Succeeds()
    {
        // The EXACT shape of the live GrandSlam SPD login blob (verified against
        // real Apple): a bare <dict>…</dict> with no <?xml?>, no DOCTYPE, and no
        // <plist> envelope. The codec must wrap + parse it.
        byte[] fragment =
            "<dict><key>isEligibleForKey</key><true/><key>adsid</key><string>000123-04-deadbeef</string><key>GsIdmsToken</key><string>tok</string><key>n</key><integer>8133149</integer></dict>"u8.ToArray();

        NSDictionary parsed = PlistCodec.ParseDictionary(fragment);
        Assert.Equal("000123-04-deadbeef", PlistCodec.GetString(parsed, "adsid"));
        Assert.Equal("tok", PlistCodec.GetString(parsed, "GsIdmsToken"));
        Assert.Equal(8133149, PlistCodec.GetLong(parsed, "n"));
    }

    [Fact]
    public void ParseDictionary_BareDictWithLeadingWhitespace_Succeeds()
    {
        byte[] fragment = "  \n<dict><key>k</key><string>v</string></dict>"u8.ToArray();
        NSDictionary parsed = PlistCodec.ParseDictionary(fragment);
        Assert.Equal("v", PlistCodec.GetString(parsed, "k"));
    }

    [Fact]
    public void ParseDictionary_Garbage_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PlistCodec.ParseDictionary("not a plist"u8.ToArray()));
    }

    [Fact]
    public void GetData_OnNonDataKey_ThrowsFormatException()
    {
        byte[] xml = PlistCodec.ToXmlBytes(new Dictionary<string, object> { ["k"] = "string-not-data" });
        NSDictionary parsed = PlistCodec.ParseDictionary(xml);
        Assert.Throws<FormatException>(() => PlistCodec.GetData(parsed, "k"));
    }

    [Fact]
    public void GetString_MissingKey_ThrowsFormatException()
    {
        byte[] xml = PlistCodec.ToXmlBytes(new Dictionary<string, object> { ["present"] = "x" });
        NSDictionary parsed = PlistCodec.ParseDictionary(xml);
        Assert.Throws<FormatException>(() => PlistCodec.GetString(parsed, "absent"));
    }
}
