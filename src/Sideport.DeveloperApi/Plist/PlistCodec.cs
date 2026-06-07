using Claunia.PropertyList;

namespace Sideport.DeveloperApi.Plist;

/// <summary>
/// Thin adapter over <c>plist-cil</c> (Claunia.PropertyList) that converts
/// between Sideport's plain CLR request shapes and Apple XML property lists, and
/// offers typed, defensive reads of response dictionaries. Isolating the library
/// here keeps the GrandSlam/dev-API code free of plist-library specifics and
/// swappable.
/// </summary>
internal static class PlistCodec
{
    // The XML prologue Apple omits from some embedded plists (e.g. the decrypted
    // SPD blob); plist-cil parses bare <plist> too, but we prepend on retry.
    private const string XmlPrologue =
        "<?xml version='1.0' encoding='UTF-8'?>\n" +
        "<!DOCTYPE plist PUBLIC '-//Apple//DTD PLIST 1.0//EN' " +
        "'http://www.apple.com/DTDs/PropertyList-1.0.dtd'>\n";

    /// <summary>Serialize a CLR object graph to an Apple XML plist (UTF-8 bytes).</summary>
    public static byte[] ToXmlBytes(object value)
    {
        NSObject root = Wrap(value);
        string xml = root.ToXmlPropertyList();
        return System.Text.Encoding.UTF8.GetBytes(xml);
    }

    /// <summary>Parse XML/binary plist bytes into an <see cref="NSDictionary"/>.</summary>
    /// <exception cref="FormatException">The bytes are not a plist dictionary.</exception>
    public static NSDictionary ParseDictionary(byte[] bytes)
    {
        NSObject parsed;
        try
        {
            parsed = PropertyListParser.Parse(bytes);
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            // Retry with the Apple XML prologue prepended (the SPD-blob case).
            try
            {
                byte[] prefixed = Combine(System.Text.Encoding.UTF8.GetBytes(XmlPrologue), bytes);
                parsed = PropertyListParser.Parse(prefixed);
            }
            catch (Exception inner)
            {
                throw new FormatException("could not parse plist payload", inner);
            }
        }

        return parsed as NSDictionary
            ?? throw new FormatException($"expected a plist dictionary, got {parsed?.GetType().Name ?? "null"}");
    }

    // --- typed reads -------------------------------------------------------

    public static bool TryGet(NSDictionary dict, string key, out NSObject value)
    {
        if (dict.ContainsKey(key))
        {
            value = dict[key];
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>
    /// Return the raw plist node for a key, preserving its type. Used for opaque
    /// values (e.g. the GrandSlam cookie <c>c</c>, which Apple sends as a string
    /// but must be echoed back unchanged) so we never coerce the wire type.
    /// </summary>
    public static NSObject GetNode(NSDictionary dict, string key) =>
        dict.ContainsKey(key)
            ? dict[key]
            : throw new FormatException($"plist key '{key}' missing");

    public static NSDictionary GetDictionary(NSDictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key] is NSDictionary nested
            ? nested
            : throw new FormatException($"plist key '{key}' is not a dictionary");

    public static IReadOnlyList<NSObject> GetArray(NSDictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key] is NSArray array
            ? [.. array]
            : throw new FormatException($"plist key '{key}' is not an array");

    /// <summary>Read an array, returning empty when the key is absent or null.</summary>
    public static IReadOnlyList<NSObject> GetArrayOrEmpty(NSDictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key] is NSArray array
            ? [.. array]
            : [];

    public static string GetString(NSDictionary dict, string key) =>
        dict.ContainsKey(key)
            ? dict[key].ToString()!
            : throw new FormatException($"plist key '{key}' missing");

    public static string? GetStringOrNull(NSDictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].ToString() : null;

    public static byte[] GetData(NSDictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key] is NSData data
            ? data.Bytes
            : throw new FormatException($"plist key '{key}' is not data");

    public static long GetLong(NSDictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key] is NSNumber number
            ? number.ToLong()
            : throw new FormatException($"plist key '{key}' is not a number");

    // --- CLR -> NSObject ---------------------------------------------------

    private static NSObject Wrap(object value) => value switch
    {
        NSObject ns => ns,
        bool b => new NSNumber(b),
        int i => new NSNumber(i),
        long l => new NSNumber(l),
        double d => new NSNumber(d),
        string s => new NSString(s),
        byte[] bytes => new NSData(bytes),
        IReadOnlyDictionary<string, object> dict => WrapDictionary(dict),
        System.Collections.IEnumerable seq => WrapArray(seq),
        _ => throw new ArgumentException($"cannot wrap {value.GetType()} into a plist node"),
    };

    private static NSDictionary WrapDictionary(IReadOnlyDictionary<string, object> dict)
    {
        var ns = new NSDictionary();
        foreach ((string key, object item) in dict)
            ns.Add(key, Wrap(item));
        return ns;
    }

    private static NSArray WrapArray(System.Collections.IEnumerable seq)
    {
        var items = new List<NSObject>();
        foreach (object item in seq)
            items.Add(Wrap(item));
        return new NSArray([.. items]);
    }

    private static byte[] Combine(byte[] a, byte[] b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
