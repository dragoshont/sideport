using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Sideport.GrandSlam.Numerics;
using Sideport.GrandSlam.Srp;

namespace Sideport.GrandSlam.Tests.Srp;

/// <summary>
/// An independent, test-only SRP-6a <em>server</em> implementing the same Apple
/// GrandSlam variant as <see cref="AppleSrpClient"/>, written from the SRP-6a
/// specification rather than from the client code. A successful mutual handshake
/// therefore cross-checks the client against a second implementation, not just
/// against the single fixed golden vector (implementation plan §P2, adversarial A2).
/// </summary>
internal sealed class TestSrpServer
{
    private static readonly byte[] Colon = ":"u8.ToArray();

    private readonly string _username;
    private readonly byte[] _salt;
    private readonly BigInteger _verifier; // v = g^x mod N
    private readonly BigInteger _b;        // server private ephemeral
    private readonly BigInteger _publicB;  // B = (k*v + g^b) mod N

    public TestSrpServer(string username, byte[] passwordKey, byte[] salt, ReadOnlySpan<byte> privateB)
    {
        _username = username;
        _salt = salt;

        BigInteger x = HashToInt(salt, Sha256(Colon, passwordKey));
        _verifier = BigInteger.ModPow(SrpGroup.G, x, SrpGroup.N);

        _b = BigEndianBytes.ToBigInteger(privateB);
        BigInteger k = HashToInt(Pad(SrpGroup.N), Pad(SrpGroup.G));
        _publicB = Mod(k * _verifier + BigInteger.ModPow(SrpGroup.G, _b, SrpGroup.N), SrpGroup.N);
    }

    public byte[] Salt => _salt;

    public byte[] PublicB => BigEndianBytes.ToFixedBytes(_publicB, SrpGroup.Width);

    /// <summary>The session key the server derives; valid after <see cref="ComputeEvidence"/>.</summary>
    public byte[] SessionKey { get; private set; } = [];

    /// <summary>
    /// Verify the client evidence <c>M1</c> against the server's own derivation
    /// and, on success, return the server evidence <c>M2</c>.
    /// </summary>
    public byte[] ComputeEvidence(ReadOnlySpan<byte> clientA, ReadOnlySpan<byte> clientM1)
    {
        BigInteger a = BigEndianBytes.ToBigInteger(clientA);
        if ((a % SrpGroup.N).IsZero)
            throw new InvalidOperationException("client A ≡ 0 (mod N)");

        BigInteger u = HashToInt(Pad(a), Pad(_publicB));
        // S = (A * v^u)^b mod N
        BigInteger s = BigInteger.ModPow(
            Mod(a * BigInteger.ModPow(_verifier, u, SrpGroup.N), SrpGroup.N), _b, SrpGroup.N);
        SessionKey = Sha256(BigEndianBytes.ToMinimalBytes(s));

        byte[] hXor = Xor(Sha256(Pad(SrpGroup.N)), Sha256(Pad(SrpGroup.G)));
        byte[] hUser = SHA256.HashData(Encoding.UTF8.GetBytes(_username));
        byte[] aMin = BigEndianBytes.ToMinimalBytes(a);
        byte[] bMin = BigEndianBytes.ToMinimalBytes(_publicB);

        byte[] expectedM1 = Sha256(hXor, hUser, _salt, aMin, bMin, SessionKey);
        if (!CryptographicOperations.FixedTimeEquals(expectedM1, clientM1))
            throw new InvalidOperationException("client M1 did not verify");

        return Sha256(aMin, expectedM1, SessionKey); // M2
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
