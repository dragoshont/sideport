using System.Diagnostics;
using Sideport.Core;

namespace Sideport.DeveloperApi;

/// <summary>
/// <see cref="ISigner"/> that shells out to a signer binary sidecar (design §6):
/// <c>zsign</c> (MIT, proven on the homelab host) by default, migrating to
/// <c>rcodesign</c> (Apache-2.0) later. The binary path comes from
/// <see cref="SignerOptions"/>.
/// </summary>
public sealed class ProcessSigner(SignerOptions options) : ISigner
{
    private readonly SignerOptions _options = options;

    public Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
    {
        // Phase 4: build the zsign/rcodesign argv from _options + request and
        // run it. Kept as a single serialized invocation (single-signer rule).
        _ = new ProcessStartInfo(_options.SignerBinaryPath);
        throw new NotImplementedException("Phase 4: invoke signer binary and parse result.");
    }
}

/// <summary>Configuration for <see cref="ProcessSigner"/>.</summary>
public sealed class SignerOptions
{
    /// <summary>Absolute path to the signer binary (zsign or rcodesign).</summary>
    public string SignerBinaryPath { get; set; } = "/opt/sideport/zsign";

    /// <summary>Which signer flavor is at <see cref="SignerBinaryPath"/>.</summary>
    public SignerKind Kind { get; set; } = SignerKind.Zsign;
}

/// <summary>Supported signer binaries.</summary>
public enum SignerKind { Zsign, Rcodesign }
