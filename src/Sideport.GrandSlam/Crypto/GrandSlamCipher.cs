using System.Security.Cryptography;

namespace Sideport.GrandSlam.Crypto;

/// <summary>
/// The symmetric primitives GrandSlam uses to unwrap response payloads:
/// AES-256-CBC (PKCS#7) for the <c>spd</c> blob and AES-256-GCM for encrypted
/// tokens. Standard BCL algorithms — no Apple code, covered by round-trip and
/// NIST-style tests.
///
/// This type intentionally exposes only the raw transforms. The GrandSlam-
/// specific framing of those blobs (field layout, AAD/IV placement) is applied
/// by the protocol layer (<c>Sideport.DeveloperApi</c>) where it can be
/// validated against real server responses, rather than hard-coded here from an
/// unverified guess.
/// </summary>
public static class GrandSlamCipher
{
    private const int Aes256KeySize = 32;
    private const int CbcIvSize = 16;
    private const int GcmTagSize = 16;

    /// <summary>Decrypt an AES-256-CBC, PKCS#7-padded ciphertext.</summary>
    public static byte[] DecryptCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext)
    {
        RequireKey(key);
        RequireIv(iv);

        using Aes aes = Aes.Create();
        aes.Key = key.ToArray();
        return aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
    }

    /// <summary>Encrypt with AES-256-CBC and PKCS#7 padding (the inverse of <see cref="DecryptCbc"/>).</summary>
    public static byte[] EncryptCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plaintext)
    {
        RequireKey(key);
        RequireIv(iv);

        using Aes aes = Aes.Create();
        aes.Key = key.ToArray();
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }

    /// <summary>
    /// Decrypt and authenticate an AES-256-GCM ciphertext. Throws
    /// <see cref="AuthenticationTagMismatchException"/> if the tag does not
    /// verify under <paramref name="key"/>/<paramref name="nonce"/>/<paramref name="associatedData"/>.
    /// </summary>
    public static byte[] DecryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        RequireKey(key);
        if (tag.Length != GcmTagSize)
            throw new ArgumentException($"GCM tag must be {GcmTagSize} bytes", nameof(tag));

        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm aes = new(key, GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    /// <summary>
    /// Encrypt with AES-256-GCM, returning the ciphertext and writing the
    /// authentication tag into <paramref name="tag"/> (the inverse of
    /// <see cref="DecryptGcm"/>).
    /// </summary>
    public static byte[] EncryptGcm(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        RequireKey(key);
        if (tag.Length != GcmTagSize)
            throw new ArgumentException($"GCM tag must be {GcmTagSize} bytes", nameof(tag));

        byte[] ciphertext = new byte[plaintext.Length];
        using AesGcm aes = new(key, GcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return ciphertext;
    }

    private static void RequireKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != Aes256KeySize)
            throw new ArgumentException($"AES-256 key must be {Aes256KeySize} bytes", nameof(key));
    }

    private static void RequireIv(ReadOnlySpan<byte> iv)
    {
        if (iv.Length != CbcIvSize)
            throw new ArgumentException($"CBC IV must be {CbcIvSize} bytes", nameof(iv));
    }
}
