using System.Security.Cryptography;
using Sideport.GrandSlam.Crypto;

namespace Sideport.GrandSlam.Tests.Crypto;

public class GrandSlamCipherTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    // --- AES-256-CBC (the spd blob transform) ---

    [Theory]
    [InlineData(0)]    // empty -> one padding block
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]   // exact block -> full padding block added
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(100)]
    public void Cbc_RoundTrips(int plaintextLength)
    {
        byte[] key = Key();
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] plaintext = RandomNumberGenerator.GetBytes(plaintextLength);

        byte[] ciphertext = GrandSlamCipher.EncryptCbc(key, iv, plaintext);
        byte[] roundTrip = GrandSlamCipher.DecryptCbc(key, iv, ciphertext);

        Assert.Equal(plaintext, roundTrip);
    }

    [Fact]
    public void Cbc_WrongKey_FailsToRecoverPlaintext()
    {
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] plaintext = RandomNumberGenerator.GetBytes(48);
        byte[] ciphertext = GrandSlamCipher.EncryptCbc(Key(), iv, plaintext);

        // A wrong key either trips PKCS#7 validation or yields different bytes;
        // either way the original plaintext must not come back.
        try
        {
            byte[] recovered = GrandSlamCipher.DecryptCbc(Key(), iv, ciphertext);
            Assert.NotEqual(plaintext, recovered);
        }
        catch (CryptographicException)
        {
            // Expected: padding validation rejected the wrong key.
        }
    }

    [Fact]
    public void Cbc_RejectsWrongKeySize()
    {
        Assert.Throws<ArgumentException>(
            () => GrandSlamCipher.EncryptCbc(new byte[16], new byte[16], []));
    }

    [Fact]
    public void Cbc_RejectsWrongIvSize()
    {
        Assert.Throws<ArgumentException>(
            () => GrandSlamCipher.EncryptCbc(Key(), new byte[12], []));
    }

    // --- AES-256-GCM (the encrypted-token transform) ---

    [Fact]
    public void Gcm_RoundTrips_WithAssociatedData()
    {
        byte[] key = Key();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] aad = "XYZ"u8.ToArray();
        byte[] plaintext = RandomNumberGenerator.GetBytes(80);
        byte[] tag = new byte[16];

        byte[] ciphertext = GrandSlamCipher.EncryptGcm(key, nonce, plaintext, tag, aad);
        byte[] roundTrip = GrandSlamCipher.DecryptGcm(key, nonce, ciphertext, tag, aad);

        Assert.Equal(plaintext, roundTrip);
    }

    [Fact]
    public void Gcm_RoundTrips_With16ByteIv()
    {
        // The GrandSlam app-token blob uses a 16-byte GCM IV — the BCL AesGcm
        // (12-byte only) cannot do this; the BouncyCastle backend must.
        byte[] key = Key();
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] aad = "XYZ"u8.ToArray();
        byte[] plaintext = RandomNumberGenerator.GetBytes(64);
        byte[] tag = new byte[16];

        byte[] ciphertext = GrandSlamCipher.EncryptGcm(key, iv, plaintext, tag, aad);
        byte[] roundTrip = GrandSlamCipher.DecryptGcm(key, iv, ciphertext, tag, aad);

        Assert.Equal(plaintext, roundTrip);
    }

    [Fact]
    public void Gcm_TamperedTag_ThrowsAuthenticationFailure()
    {
        byte[] key = Key();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = RandomNumberGenerator.GetBytes(32);
        byte[] tag = new byte[16];
        byte[] ciphertext = GrandSlamCipher.EncryptGcm(key, nonce, plaintext, tag);

        tag[0] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(
            () => GrandSlamCipher.DecryptGcm(key, nonce, ciphertext, tag));
    }

    [Fact]
    public void Gcm_WrongAssociatedData_ThrowsAuthenticationFailure()
    {
        byte[] key = Key();
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = RandomNumberGenerator.GetBytes(32);
        byte[] tag = new byte[16];
        byte[] ciphertext = GrandSlamCipher.EncryptGcm(key, nonce, plaintext, tag, "XYZ"u8);

        Assert.Throws<AuthenticationTagMismatchException>(
            () => GrandSlamCipher.DecryptGcm(key, nonce, ciphertext, tag, "ZYX"u8));
    }

    [Fact]
    public void Gcm_RejectsWrongKeySize()
    {
        Assert.Throws<ArgumentException>(
            () => GrandSlamCipher.EncryptGcm(new byte[16], new byte[12], [], new byte[16]));
    }

    [Fact]
    public void Gcm_RejectsWrongTagSize()
    {
        Assert.Throws<ArgumentException>(
            () => GrandSlamCipher.EncryptGcm(Key(), new byte[12], [], new byte[12]));
    }
}
