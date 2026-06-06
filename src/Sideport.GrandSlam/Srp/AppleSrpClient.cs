using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Sideport.GrandSlam.Numerics;

namespace Sideport.GrandSlam.Srp;

/// <summary>
/// SRP-6a client for the Apple GrandSlam variant, in pure managed BCL — no
/// native <c>corecrypto</c> or <c>libgsa</c> at runtime (design §6 reversal).
///
/// The exact variant is proven byte-for-byte against the libgsa golden vectors
/// (which were in turn derived from the MIT <c>srp</c> library that authenticates
/// live against Apple GSA):
/// <code>
///   A  = g^a mod N
///   k  = H(PAD(N) | PAD(g))
///   u  = H(PAD(A) | PAD(B))
///   x  = H(salt | H(":" | passwordKey))          // noUsernameInX keeps the ":" sep
///   S  = (B - k*g^x)^(a + u*x) mod N
///   K  = H(minimal(S))                            // session key
///   M1 = H( H(N) XOR H(g) | H(username) | salt | minimal(A) | minimal(B) | K )
///   M2 = H( minimal(A) | M1 | K )                 // server evidence
/// </code>
/// Only <c>k</c> and <c>u</c> take <c>PAD</c>-ed (fixed-width) inputs; <c>A</c>,
/// <c>B</c> and <c>S</c> enter the hashes as minimal big-endian bytes.
///
/// One instance performs one handshake and is not thread-safe.
///
/// <para>
/// Side-channel posture: the modular exponentiations use
/// <see cref="BigInteger.ModPow"/>, which is not constant-time. This is
/// deliberate and acceptable here — the private exponent <c>a</c> is ephemeral
/// and single-use, the service is self-hosted and single-tenant (no remote
/// timing oracle), and recovering <c>a + u·x</c> by timing would still not
/// reveal the password (<c>a</c> is discarded after the handshake). This
/// matches the OpenSSL/corecrypto reference, which likewise does not set a
/// constant-time flag on this path.
/// </para>
/// </summary>
public sealed class AppleSrpClient
{
    private static readonly byte[] ColonSeparator = ":"u8.ToArray();

    private readonly BigInteger _a;        // client private ephemeral
    private readonly BigInteger _publicA;  // A = g^a mod N

    private byte[]? _sessionKey;           // K = H(S)
    private byte[]? _m1;                   // client evidence

    /// <summary>
    /// Create a client with a cryptographically-random 256-bit private
    /// ephemeral. This is the constructor production code should use.
    /// </summary>
    public AppleSrpClient() : this(GeneratePrivateExponent())
    {
    }

    /// <summary>
    /// Create a client with a caller-supplied private ephemeral. Internal and
    /// used only by the deterministic golden-vector tests; production code must
    /// never pin the private exponent.
    /// </summary>
    internal AppleSrpClient(ReadOnlySpan<byte> privateExponent)
    {
        _a = BigEndianBytes.ToBigInteger(privateExponent);
        if (_a.IsZero)
            throw new ArgumentException("private exponent must be non-zero", nameof(privateExponent));

        _publicA = BigInteger.ModPow(SrpGroup.G, _a, SrpGroup.N);
        if ((_publicA % SrpGroup.N).IsZero)
            throw new SrpProtocolException("computed client public value A ≡ 0 (mod N)");
    }

    /// <summary>
    /// Begin authentication: returns the client public value <c>A</c> as
    /// fixed-width (256-byte) big-endian, ready to send to the server.
    /// </summary>
    public byte[] StartAuthentication() => BigEndianBytes.ToFixedBytes(_publicA, SrpGroup.Width);

    /// <summary>
    /// Process the server challenge and produce the client evidence <c>M1</c>.
    /// </summary>
    /// <param name="username">The Apple ID — folded into M1 via <c>H(username)</c>.</param>
    /// <param name="passwordKey">The s2k password key (see <see cref="GrandSlamCrypto.DerivePasswordKey"/>).</param>
    /// <param name="salt">The server-provided salt.</param>
    /// <param name="serverPublicB">The server public value <c>B</c>, big-endian.</param>
    /// <returns><c>M1</c> (32 bytes) to send to the server.</returns>
    /// <exception cref="SrpProtocolException">A protocol safety check failed.</exception>
    public byte[] ProcessChallenge(
        string username,
        ReadOnlySpan<byte> passwordKey,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> serverPublicB)
    {
        ArgumentNullException.ThrowIfNull(username);

        BigInteger b = BigEndianBytes.ToBigInteger(serverPublicB);
        if ((b % SrpGroup.N).IsZero)
            throw new SrpProtocolException("server public value B ≡ 0 (mod N)");

        byte[] saltBytes = salt.ToArray();
        byte[] nPad = BigEndianBytes.ToFixedBytes(SrpGroup.N, SrpGroup.Width);
        byte[] gPad = BigEndianBytes.ToFixedBytes(SrpGroup.G, SrpGroup.Width);
        byte[] aPad = BigEndianBytes.ToFixedBytes(_publicA, SrpGroup.Width);
        byte[] bPad = BigEndianBytes.ToFixedBytes(b, SrpGroup.Width);

        // k = H(PAD(N) | PAD(g))
        BigInteger k = HashToInt(nPad, gPad);

        // u = H(PAD(A) | PAD(B))
        BigInteger u = HashToInt(aPad, bPad);
        if (u.IsZero)
            throw new SrpProtocolException("scrambling parameter u = 0");

        // x = H(salt | H(":" | passwordKey)) — noUsernameInX retains the colon.
        byte[] inner = Sha256(ColonSeparator, passwordKey.ToArray());
        BigInteger x = HashToInt(saltBytes, inner);

        // S = (B - k*g^x)^(a + u*x) mod N
        BigInteger gx = BigInteger.ModPow(SrpGroup.G, x, SrpGroup.N);
        BigInteger kgx = (k * gx) % SrpGroup.N;
        BigInteger baseValue = Mod(b - kgx, SrpGroup.N); // keep non-negative
        BigInteger exponent = _a + u * x;
        BigInteger s = BigInteger.ModPow(baseValue, exponent, SrpGroup.N);

        // K = H(minimal(S))
        _sessionKey = SHA256.HashData(BigEndianBytes.ToMinimalBytes(s));

        // M1 = H( H(N) XOR H(g) | H(username) | salt | minimal(A) | minimal(B) | K )
        byte[] hN = SHA256.HashData(nPad);
        byte[] hG = SHA256.HashData(gPad);
        byte[] hXor = Xor(hN, hG);
        byte[] hUsername = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        byte[] aMin = BigEndianBytes.ToMinimalBytes(_publicA);
        byte[] bMin = BigEndianBytes.ToMinimalBytes(b);

        _m1 = Sha256(hXor, hUsername, saltBytes, aMin, bMin, _sessionKey);
        return (byte[])_m1.Clone();
    }

    /// <summary>
    /// Verify the server evidence <c>M2 = H(minimal(A) | M1 | K)</c> in constant
    /// time. Returns <see langword="false"/> on mismatch (a forged or wrong server).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ProcessChallenge"/> has not been called yet.
    /// </exception>
    public bool VerifyServerEvidence(ReadOnlySpan<byte> serverM2)
    {
        if (_m1 is null || _sessionKey is null)
            throw new InvalidOperationException(
                $"call {nameof(ProcessChallenge)} before verifying server evidence");

        byte[] aMin = BigEndianBytes.ToMinimalBytes(_publicA);
        byte[] expected = Sha256(aMin, _m1, _sessionKey);
        return CryptographicOperations.FixedTimeEquals(expected, serverM2);
    }

    /// <summary>
    /// The negotiated session key <c>K = H(S)</c> (32 bytes), available after
    /// <see cref="ProcessChallenge"/>. Used to derive the GrandSlam extra-data
    /// keys (see <see cref="GrandSlamSessionKeys"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ProcessChallenge"/> has not been called yet.
    /// </exception>
    public byte[] SessionKey =>
        _sessionKey is null
            ? throw new InvalidOperationException(
                $"session key is not available until {nameof(ProcessChallenge)} has run")
            : (byte[])_sessionKey.Clone();

    // --- helpers -----------------------------------------------------------

    private static BigInteger HashToInt(byte[] first, byte[] second) =>
        BigEndianBytes.ToBigInteger(Sha256(first, second));

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

    /// <summary>Euclidean remainder in <c>[0, modulus)</c> even for a negative dividend.</summary>
    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        BigInteger r = value % modulus;
        return r.Sign < 0 ? r + modulus : r;
    }

    private static byte[] GeneratePrivateExponent()
    {
        // 256-bit random private exponent with the high bit set, matching the
        // libgsa reference (BN_rand(256, BN_RAND_TOP_ONE)): guarantees a full
        // 256-bit, non-zero value.
        byte[] a = RandomNumberGenerator.GetBytes(32);
        a[0] |= 0x80;
        return a;
    }
}
