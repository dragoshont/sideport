using System.Security.Cryptography;
using Sideport.GrandSlam;
using Sideport.GrandSlam.Numerics;
using Sideport.GrandSlam.Srp;
using Sideport.GrandSlam.Tests.Vectors;

namespace Sideport.GrandSlam.Tests.Srp;

/// <summary>
/// The Phase-2 keystone: the managed SRP-6a client must reproduce the Apple
/// GrandSlam variant byte-for-byte against the libgsa golden vector, and must
/// also complete a live mutual handshake against an independent SRP server.
/// </summary>
public class AppleSrpClientTests
{
    // --- Golden-vector pinning (byte-for-byte vs libgsa / the MIT srp oracle) ---

    [Fact]
    public void StartAuthentication_MatchesGoldenVectorA()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        Assert.Equal(AppleSrpVector.PublicA, client.StartAuthentication());
    }

    [Fact]
    public void ProcessChallenge_MatchesGoldenVectorM1()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        client.StartAuthentication();

        byte[] m1 = client.ProcessChallenge(
            AppleSrpVector.Username,
            AppleSrpVector.PasswordKey,
            AppleSrpVector.Salt,
            AppleSrpVector.FixedB);

        Assert.Equal(AppleSrpVector.M1, m1);
    }

    [Fact]
    public void ProcessChallenge_DerivesGoldenVectorSessionKey()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        client.ProcessChallenge(
            AppleSrpVector.Username,
            AppleSrpVector.PasswordKey,
            AppleSrpVector.Salt,
            AppleSrpVector.FixedB);

        Assert.Equal(AppleSrpVector.SessionKey, client.SessionKey);
    }

    [Fact]
    public void VerifyServerEvidence_AcceptsGoldenVectorM2()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        client.ProcessChallenge(
            AppleSrpVector.Username,
            AppleSrpVector.PasswordKey,
            AppleSrpVector.Salt,
            AppleSrpVector.FixedB);

        Assert.True(client.VerifyServerEvidence(AppleSrpVector.M2));
    }

    [Fact]
    public void ProcessChallenge_FromPassword_EndToEnd_MatchesVector()
    {
        // Derive the s2k password key from the raw password, exactly as a real
        // client does once it has the server's salt + iteration count.
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey(
            AppleSrpVector.Password, AppleSrpVector.Salt, AppleSrpVector.Iterations);
        Assert.Equal(AppleSrpVector.PasswordKey, passwordKey);

        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        byte[] m1 = client.ProcessChallenge(
            AppleSrpVector.Username, passwordKey, AppleSrpVector.Salt, AppleSrpVector.FixedB);

        Assert.Equal(AppleSrpVector.M1, m1);
    }

    // --- Server-evidence rejection ---

    [Fact]
    public void VerifyServerEvidence_RejectsWrongM2()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        client.ProcessChallenge(
            AppleSrpVector.Username,
            AppleSrpVector.PasswordKey,
            AppleSrpVector.Salt,
            AppleSrpVector.FixedB);

        byte[] tampered = AppleSrpVector.M2;
        tampered[0] ^= 0xFF;
        Assert.False(client.VerifyServerEvidence(tampered));
    }

    // --- Independent mutual handshake (random a and b) ---

    [Fact]
    public void RandomHandshake_AgainstIndependentServer_BothSidesAgree()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        const string username = "person@example.com";
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey("a real-ish password", salt, 1000);

        for (int round = 0; round < 8; round++)
        {
            var client = new AppleSrpClient(); // fresh random private exponent
            byte[] a = client.StartAuthentication();

            byte[] privateB = RandomNumberGenerator.GetBytes(32);
            var server = new TestSrpServer(username, passwordKey, salt, privateB);

            byte[] m1 = client.ProcessChallenge(username, passwordKey, server.Salt, server.PublicB);
            byte[] m2 = server.ComputeEvidence(a, m1);

            Assert.True(client.VerifyServerEvidence(m2), $"round {round}: M2 did not verify");
            Assert.Equal(server.SessionKey, client.SessionKey);
        }
    }

    [Fact]
    public void Handshake_WrongPassword_ServerRejectsM1()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        const string username = "person@example.com";
        byte[] serverKey = GrandSlamCrypto.DerivePasswordKey("the right password", salt, 1000);
        byte[] clientKey = GrandSlamCrypto.DerivePasswordKey("the WRONG password", salt, 1000);

        var client = new AppleSrpClient();
        byte[] a = client.StartAuthentication();
        var server = new TestSrpServer(username, serverKey, salt, RandomNumberGenerator.GetBytes(32));

        byte[] m1 = client.ProcessChallenge(username, clientKey, server.Salt, server.PublicB);

        Assert.Throws<InvalidOperationException>(() => server.ComputeEvidence(a, m1));
    }

    // --- Protocol-safety and contract checks ---

    [Fact]
    public void ProcessChallenge_ServerBCongruentToZero_Throws()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        byte[] bEqualsN = BigEndianBytes.ToFixedBytes(SrpGroup.N, SrpGroup.Width); // N ≡ 0 (mod N)

        Assert.Throws<SrpProtocolException>(() => client.ProcessChallenge(
            AppleSrpVector.Username, AppleSrpVector.PasswordKey, AppleSrpVector.Salt, bEqualsN));
    }

    [Fact]
    public void VerifyServerEvidence_BeforeProcessChallenge_Throws()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        Assert.Throws<InvalidOperationException>(
            () => client.VerifyServerEvidence(AppleSrpVector.M2));
    }

    [Fact]
    public void SessionKey_BeforeProcessChallenge_Throws()
    {
        var client = new AppleSrpClient(AppleSrpVector.FixedA);
        Assert.Throws<InvalidOperationException>(() => _ = client.SessionKey);
    }

    [Fact]
    public void Constructor_ZeroExponent_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AppleSrpClient(new byte[32]));
    }

    [Fact]
    public void DefaultConstructor_ProducesFullWidthPublicValue()
    {
        var client = new AppleSrpClient();
        Assert.Equal(SrpGroup.Width, client.StartAuthentication().Length);
    }
}
