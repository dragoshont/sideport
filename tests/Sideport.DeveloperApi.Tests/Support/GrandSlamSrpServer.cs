using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Sideport.GrandSlam.Numerics;
using Sideport.GrandSlam.Srp;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// An independent SRP-6a <em>server</em> for the Apple GrandSlam variant, used
/// to drive <c>GrandSlamClient</c> end-to-end through a fake transport. It is
/// written from the SRP spec (verifier formulation), not from the client code,
/// so a successful handshake cross-checks the client against a second
/// implementation — and it independently re-derives the SPD encryption keys, so
/// the CBC decrypt path is a true cross-check too.
/// </summary>
internal sealed class GrandSlamSrpServer
{
    private static readonly byte[] Colon = ":"u8.ToArray();

    private readonly string _username;
    private readonly BigInteger _verifier;
    private readonly byte[] _salt;
    private readonly int _iterations;

    private BigInteger _b;
    private BigInteger _publicB;
    private byte[] _sessionKey = [];

    public GrandSlamSrpServer(string username, byte[] passwordKey, byte[] salt, int iterations)
    {
        _username = username;
        _salt = salt;
        _iterations = iterations;

        BigInteger x = HashToInt(salt, Sha256(Colon, passwordKey));
        _verifier = BigInteger.ModPow(SrpGroup.G, x, SrpGroup.N);
    }

    public byte[] Salt => _salt;
    public int Iterations => _iterations;

    /// <summary>Begin: pick a random server ephemeral and compute <c>B</c>.</summary>
    public byte[] StartChallenge()
    {
        _b = BigEndianBytes.ToBigInteger(RandomNumberGenerator.GetBytes(32));
        BigInteger k = HashToInt(Pad(SrpGroup.N), Pad(SrpGroup.G));
        _publicB = Mod(k * _verifier + BigInteger.ModPow(SrpGroup.G, _b, SrpGroup.N), SrpGroup.N);
        return BigEndianBytes.ToFixedBytes(_publicB, SrpGroup.Width);
    }

    /// <summary>
    /// Verify the client evidence <c>M1</c> for the supplied client public value
    /// <c>A</c>, derive the session key, and return the server evidence <c>M2</c>.
    /// Throws if <c>M1</c> does not verify (the wrong-password path).
    /// </summary>
    public byte[] CompleteHandshake(byte[] clientA, byte[] clientM1)
    {
        BigInteger a = BigEndianBytes.ToBigInteger(clientA);
        BigInteger u = HashToInt(Pad(a), Pad(_publicB));
        BigInteger s = BigInteger.ModPow(
            Mod(a * BigInteger.ModPow(_verifier, u, SrpGroup.N), SrpGroup.N), _b, SrpGroup.N);
        _sessionKey = Sha256(BigEndianBytes.ToMinimalBytes(s));

        byte[] hXor = Xor(Sha256(Pad(SrpGroup.N)), Sha256(Pad(SrpGroup.G)));
        byte[] hUser = SHA256.HashData(Encoding.UTF8.GetBytes(_username));
        byte[] aMin = BigEndianBytes.ToMinimalBytes(a);
        byte[] bMin = BigEndianBytes.ToMinimalBytes(_publicB);

        byte[] expectedM1 = Sha256(hXor, hUser, _salt, aMin, bMin, _sessionKey);
        if (!CryptographicOperations.FixedTimeEquals(expectedM1, clientM1))
            throw new InvalidOperationException("client M1 did not verify (wrong password)");

        return Sha256(aMin, expectedM1, _sessionKey); // M2
    }

    /// <summary>
    /// Encrypt an SPD plaintext with keys re-derived independently from the
    /// session key (HMAC labels), as Apple does, so the client's decrypt is a
    /// genuine cross-check.
    /// </summary>
    public byte[] EncryptSpd(byte[] plaintext)
    {
        byte[] edk = HMACSHA256.HashData(_sessionKey, "extra data key:"u8.ToArray());
        byte[] ediv = HMACSHA256.HashData(_sessionKey, "extra data iv:"u8.ToArray())[..16];

        using Aes aes = Aes.Create();
        aes.Key = edk;
        return aes.EncryptCbc(plaintext, ediv, PaddingMode.PKCS7);
    }

    private static byte[] Pad(BigInteger value) => BigEndianBytes.ToFixedBytes(value, SrpGroup.Width);

    private static BigInteger HashToInt(byte[] a, byte[] b) => BigEndianBytes.ToBigInteger(Sha256(a, b));

    private static byte[] Sha256(params byte[][] parts)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] part in parts)
            hash.AppendData(part);
        return hash.GetHashAndReset();
    }

    private static byte[] Xor(byte[] left, byte[] right)
    {
        byte[] result = new byte[left.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)(left[i] ^ right[i]);
        return result;
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        BigInteger r = value % modulus;
        return r.Sign < 0 ? r + modulus : r;
    }
}
