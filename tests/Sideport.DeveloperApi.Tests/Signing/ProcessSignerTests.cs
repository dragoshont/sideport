using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.Signing;

public class ProcessSignerTests
{
    private static string Touch(string dir, string name, byte[]? content = null)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content ?? [0]);
        return path;
    }

    // --- argv construction (cross-platform, no process launch) -------------

    [Fact]
    public void BuildArguments_Zsign_HasExpectedFlagsAndEmptyPassword()
    {
        var signer = new ProcessSigner(new SignerOptions { Kind = SignerKind.Zsign });
        var request = new SignRequest("in.ipa", "out.ipa", "id.p12", "p.mobileprovision", "secret");

        string[] argv = [.. signer.BuildArguments(request, "prepared.p12")];

        // The prepared (password-less) p12 is used and -p is empty: the real
        // password is never on the command line.
        Assert.Equal(
            ["-k", "prepared.p12", "-p", "", "-m", "p.mobileprovision", "-o", "out.ipa", "in.ipa"],
            argv);
        Assert.DoesNotContain("secret", argv);
    }

    [Fact]
    public void BuildArguments_Rcodesign_UsesSubcommandAndPreparedIdentity()
    {
        var signer = new ProcessSigner(new SignerOptions { Kind = SignerKind.Rcodesign });
        var request = new SignRequest("in.ipa", "out.ipa", "id.p12", "p.mobileprovision", "secret");

        string[] argv = [.. signer.BuildArguments(request, "prepared.p12")];

        Assert.Equal("sign", argv[0]);
        Assert.Contains("--p12-file", argv);
        Assert.Contains("prepared.p12", argv);
        Assert.Contains("--provisioning-profile", argv);
        Assert.Equal("out.ipa", argv[^1]);
        Assert.DoesNotContain("secret", argv);
    }

    [Fact]
    public void SignRequest_ToString_RedactsPassword()
    {
        var request = new SignRequest("in.ipa", "out.ipa", "id.p12", "p.mobileprovision", "topsecret");
        Assert.DoesNotContain("topsecret", request.ToString());
        Assert.Contains("***", request.ToString());
    }

    // --- missing-input handling (cross-platform) ---------------------------

    [Fact]
    public async Task SignAsync_MissingInputIpa_ReturnsFailure()
    {
        string dir = NewDir();
        try
        {
            var signer = new ProcessSigner(
                new SignerOptions { SignerBinaryPath = "/bin/true" }, NullLogger<ProcessSigner>.Instance);
            var request = new SignRequest(
                Path.Combine(dir, "missing.ipa"),
                Path.Combine(dir, "out.ipa"),
                Touch(dir, "id.p12"),
                Touch(dir, "p.mobileprovision"));

            SignResult result = await signer.SignAsync(request);

            Assert.False(result.Success);
            Assert.Contains("input IPA not found", result.Error);
        }
        finally { Cleanup(dir); }
    }

    // --- full process e2e via the fake signer (Unix) -----------------------

    [Fact]
    public async Task SignAsync_FakeSignerSuccess_ProducesVerifiedIpa()
    {
        if (!FakeSigner.Supported) return; // Unix-only harness

        string dir = NewDir();
        using var fake = FakeSigner.Success();
        try
        {
            byte[] ipaBytes = new TestIpaBuilder { BundleIdentifier = "com.example.signed" }.Build();
            string input = Path.Combine(dir, "in.ipa");
            File.WriteAllBytes(input, ipaBytes);
            string p12 = TestPkcs12Builder.WriteP12(dir, "p12pass");

            var signer = new ProcessSigner(
                new SignerOptions { SignerBinaryPath = fake.BinaryPath, Kind = SignerKind.Zsign },
                NullLogger<ProcessSigner>.Instance);
            var request = new SignRequest(
                input,
                Path.Combine(dir, "out.ipa"),
                p12,
                Touch(dir, "p.mobileprovision"),
                "p12pass");

            SignResult result = await signer.SignAsync(request);

            Assert.True(result.Success, result.Error);
            Assert.Equal("com.example.signed", result.BundleId);
            Assert.True(File.Exists(result.OutputIpaPath));

            // The signer received a prepared p12 and the real password never
            // appeared on the command line.
            string[] argv = fake.RecordedArgv();
            Assert.Contains("-m", argv);
            Assert.DoesNotContain("p12pass", argv);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task SignAsync_WrongP12Password_ReturnsFailure()
    {
        string dir = NewDir();
        try
        {
            string p12 = TestPkcs12Builder.WriteP12(dir, "correct-pass");
            string input = Path.Combine(dir, "in.ipa");
            File.WriteAllBytes(input, new TestIpaBuilder().Build());

            var signer = new ProcessSigner(
                new SignerOptions { SignerBinaryPath = "/bin/true" }, NullLogger<ProcessSigner>.Instance);
            var request = new SignRequest(
                input, Path.Combine(dir, "out.ipa"),
                p12, Touch(dir, "p.mobileprovision"), "WRONG-pass");

            SignResult result = await signer.SignAsync(request);

            Assert.False(result.Success);
            Assert.Contains("signing identity", result.Error);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task SignAsync_FakeSignerFailure_ReturnsErrorFromStderr()
    {
        if (!FakeSigner.Supported) return;

        string dir = NewDir();
        using var fake = FakeSigner.Failure();
        try
        {
            string input = Path.Combine(dir, "in.ipa");
            File.WriteAllBytes(input, new TestIpaBuilder().Build());
            string p12 = TestPkcs12Builder.WriteP12(dir, "pw");

            var signer = new ProcessSigner(
                new SignerOptions { SignerBinaryPath = fake.BinaryPath },
                NullLogger<ProcessSigner>.Instance);
            var request = new SignRequest(
                input, Path.Combine(dir, "out.ipa"),
                p12, Touch(dir, "p.mobileprovision"), "pw");

            SignResult result = await signer.SignAsync(request);

            Assert.False(result.Success);
            Assert.Contains("simulated failure", result.Error);
            Assert.Null(result.BundleId);
        }
        finally { Cleanup(dir); }
    }

    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-signer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
