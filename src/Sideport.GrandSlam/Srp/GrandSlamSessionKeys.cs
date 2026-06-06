using System.Security.Cryptography;
using System.Text;

namespace Sideport.GrandSlam.Srp;

/// <summary>
/// The GrandSlam extra-data keys derived from the SRP session key <c>K</c>.
/// Apple derives each as <c>HMAC-SHA256(K, label)</c> over a fixed ASCII label.
/// The <c>spd</c> blob in the login response is decrypted with
/// (<see cref="ExtraDataKey"/>, <see cref="ExtraDataIv"/>) under AES-256-CBC.
///
/// Proven against the libgsa golden vectors (EDK/EDIV/HMK).
/// </summary>
public sealed class GrandSlamSessionKeys
{
    // Apple GrandSlam HKDF-style labels (exact ASCII, including the trailing colon).
    private const string ExtraDataKeyLabel = "extra data key:";
    private const string ExtraDataIvLabel = "extra data iv:";
    private const string HmacKeyLabel = "HMAC key:";

    /// <summary>The SRP session key <c>K = H(S)</c> (32 bytes).</summary>
    public byte[] SessionKey { get; }

    /// <summary>AES-256 key for the <c>spd</c> CBC blob: <c>HMAC(K, "extra data key:")</c> (32 bytes).</summary>
    public byte[] ExtraDataKey { get; }

    /// <summary>AES IV for the <c>spd</c> CBC blob: first 16 bytes of <c>HMAC(K, "extra data iv:")</c>.</summary>
    public byte[] ExtraDataIv { get; }

    /// <summary>HMAC key for request checksums: <c>HMAC(K, "HMAC key:")</c> (32 bytes).</summary>
    public byte[] HmacKey { get; }

    /// <summary>Derive the extra-data keys from an SRP session key.</summary>
    /// <param name="sessionKey">The 32-byte SRP session key <c>K</c>.</param>
    public GrandSlamSessionKeys(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.IsEmpty)
            throw new ArgumentException("session key must not be empty", nameof(sessionKey));

        SessionKey = sessionKey.ToArray();
        ExtraDataKey = Subkey(SessionKey, ExtraDataKeyLabel);
        ExtraDataIv = Subkey(SessionKey, ExtraDataIvLabel)[..16];
        HmacKey = Subkey(SessionKey, HmacKeyLabel);
    }

    private static byte[] Subkey(byte[] key, string label) =>
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(label));
}
