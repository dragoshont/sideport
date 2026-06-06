using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Sideport.DeveloperApi;

/// <summary>
/// Prepares a signing identity for a signer binary without ever putting the
/// PKCS#12 password on the process command line.
///
/// zsign (and most signer CLIs) only accept the p12 password via argv (<c>-p</c>),
/// which is world-readable through <c>/proc/&lt;pid&gt;/cmdline</c> on a shared
/// host (OWASP A02). To avoid that, we load the identity in-process and re-export
/// it as a <em>password-less</em> PKCS#12 into a <c>0600</c> file inside a
/// <c>0700</c> per-call temp directory, then hand the signer that file with an
/// empty password. The transient key is uid-private and securely removed on
/// <see cref="Dispose"/>; the caller's original encrypted p12 is untouched.
/// </summary>
internal sealed class PreparedSigningIdentity : IDisposable
{
    private readonly string _dir;

    /// <summary>Path to the password-less PKCS#12 the signer should consume.</summary>
    public string Pkcs12Path { get; }

    private PreparedSigningIdentity(string dir, string pkcs12Path)
    {
        _dir = dir;
        Pkcs12Path = pkcs12Path;
    }

    /// <summary>
    /// Load <paramref name="sourcePkcs12Path"/> with <paramref name="password"/>
    /// and re-export it password-less into a private temp file.
    /// </summary>
    public static PreparedSigningIdentity Create(string sourcePkcs12Path, string password)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir, directory: true);

        try
        {
            // Exportable so we can re-emit the chain password-less. (We avoid
            // EphemeralKeySet: it is unsupported for PKCS#12 import on macOS and
            // some Linux configs; the key lands in the per-user keychain/keyset
            // only transiently and is disposed below.)
            X509Certificate2Collection chain = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                sourcePkcs12Path,
                password,
                X509KeyStorageFlags.Exportable);

            try
            {
                // Export the full chain password-less (empty password, not null —
                // some consumers reject a null password on PKCS#12).
                byte[] reexported = chain.Export(X509ContentType.Pkcs12, string.Empty)
                    ?? throw new InvalidOperationException("failed to re-export signing identity");

                string outPath = Path.Combine(dir, "identity.p12");
                File.WriteAllBytes(outPath, reexported);
                RestrictToOwner(outPath, directory: false);

                return new PreparedSigningIdentity(dir, outPath);
            }
            finally
            {
                foreach (X509Certificate2 cert in chain)
                    cert.Dispose();
            }
        }
        catch
        {
            TryDelete(dir);
            throw;
        }
    }

    private static void RestrictToOwner(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
            return; // NTFS ACLs differ; the Linux/macOS host is the deployment target.

        UnixFileMode mode = directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
    }

    public void Dispose() => TryDelete(_dir);

    private static void TryDelete(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;

            // Best-effort overwrite of the transient key before unlinking.
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    long length = new FileInfo(file).Length;
                    if (length > 0)
                        File.WriteAllBytes(file, new byte[length]);
                }
                catch
                {
                    // Overwrite is best-effort; deletion below is the guarantee.
                }
            }

            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
