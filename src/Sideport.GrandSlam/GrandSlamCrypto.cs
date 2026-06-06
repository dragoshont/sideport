using System.Security.Cryptography;
using System.Text;

namespace Sideport.GrandSlam;

/// <summary>
/// Clean-room GrandSlam crypto primitives (design §6 crypto reversal):
/// pure managed BCL only — no native libgsa at runtime. libgsa remains the
/// test oracle (see <c>Sideport.GrandSlam.Tests</c>).
///
/// The exact Apple SRP-6a variant is documented and proven byte-for-byte in
/// libgsa (19/19 vs the MIT <c>srp</c> oracle derived from pypush):
///   k = H(PAD(N) | PAD(g));  u = H(PAD(A) | PAD(B))
///   x = H(salt | H(":" + passwordKey))   // noUsernameInX keeps the ":" sep
///   S = (B - k*g^x)^(a + u*x);  K = SHA256(minimal(S))
///   M1 = H(H(N)^H(g) | SHA256(user) | salt | minimal(A) | minimal(B) | K)
///   M2 = H(minimal(A) | M1 | K)
/// A/B/S enter MINIMAL big-endian; only k,u pad to len(N)=256.
/// </summary>
public static class GrandSlamCrypto
{
    /// <summary>
    /// Apple "s2k" password key: PBKDF2-HMAC-SHA256 over SHA256(password),
    /// 32-byte output. ("s2k_fo" variant hex-expands the inner hash to 64B.)
    /// </summary>
    public static byte[] DerivePasswordKey(
        string password, ReadOnlySpan<byte> salt, int iterations, bool hexExpand = false)
    {
        byte[] inner = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        byte[] final = hexExpand
            ? Encoding.ASCII.GetBytes(Convert.ToHexStringLower(inner))
            : inner;

        return Rfc2898DeriveBytes.Pbkdf2(
            final, salt.ToArray(), iterations, HashAlgorithmName.SHA256, 32);
    }

    /// <summary>Left-pad a big-endian magnitude to <paramref name="length"/> bytes.</summary>
    public static byte[] Pad(ReadOnlySpan<byte> value, int length)
    {
        if (value.Length > length)
            throw new ArgumentOutOfRangeException(nameof(value), "value longer than pad length");
        byte[] padded = new byte[length];
        value.CopyTo(padded.AsSpan(length - value.Length));
        return padded;
    }
}
