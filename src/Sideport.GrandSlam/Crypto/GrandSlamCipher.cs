using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

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
    /// Decrypt and authenticate an AES-256-GCM ciphertext. Supports an
    /// arbitrary-length IV/nonce (the GrandSlam app-token blob uses a 16-byte
    /// IV, which the BCL <see cref="AesGcm"/> — 12-byte nonce only — rejects).
    /// Throws on tag-verification failure.
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

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(
            new KeyParameter(key.ToArray()), GcmTagSize * 8, nonce.ToArray(), associatedData.ToArray()));

        // BouncyCastle's AEAD consumes the ciphertext with the tag appended.
        byte[] input = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(input);
        tag.CopyTo(input.AsSpan(ciphertext.Length));

        byte[] output = new byte[cipher.GetOutputSize(input.Length)];
        int written = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        try
        {
            written += cipher.DoFinal(output, written);
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException ex)
        {
            // Preserve the BCL contract: a tag/AAD mismatch surfaces as the
            // standard authentication-tag exception regardless of the backend.
            throw new AuthenticationTagMismatchException("GCM authentication failed", ex);
        }
        return written == output.Length ? output : output[..written];
    }

    /// <summary>
    /// Encrypt with AES-256-GCM (arbitrary-length IV), returning the ciphertext
    /// and writing the authentication tag into <paramref name="tag"/> (the
    /// inverse of <see cref="DecryptGcm"/>).
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

        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(
            new KeyParameter(key.ToArray()), GcmTagSize * 8, nonce.ToArray(), associatedData.ToArray()));

        byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
        int written = cipher.ProcessBytes(plaintext.ToArray(), 0, plaintext.Length, output, 0);
        cipher.DoFinal(output, written);

        // output = ciphertext || tag; split the trailing tag out.
        int ciphertextLength = output.Length - GcmTagSize;
        output.AsSpan(ciphertextLength, GcmTagSize).CopyTo(tag);
        return output[..ciphertextLength];
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
