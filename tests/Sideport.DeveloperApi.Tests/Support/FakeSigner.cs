using System.Runtime.InteropServices;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// Writes a throwaway "signer" executable (a shell script) that records the argv
/// it was invoked with and, on the success path, produces an output file by
/// copying the input — letting <c>ProcessSigner</c> be driven end-to-end (argv
/// construction, process execution, exit handling, output verification) without
/// a real signer binary. Unix-only (the project targets Linux/macOS hosts).
/// </summary>
internal sealed class FakeSigner : IDisposable
{
    private readonly string _dir;

    public string BinaryPath { get; }
    public string ArgsLogPath { get; }

    private FakeSigner(string dir, string binaryPath, string argsLogPath)
    {
        _dir = dir;
        BinaryPath = binaryPath;
        ArgsLogPath = argsLogPath;
    }

    public static bool Supported => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// A signer that succeeds: records argv, then copies the input IPA (last
    /// argument) to the path following <c>-o</c>.
    /// </summary>
    public static FakeSigner Success() => Create(succeed: true);

    /// <summary>A signer that fails: records argv, prints to stderr, exits non-zero.</summary>
    public static FakeSigner Failure() => Create(succeed: false);

    private static FakeSigner Create(bool succeed)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-fakesigner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string argsLog = Path.Combine(dir, "argv.txt");
        string binary = Path.Combine(dir, "fakesigner.sh");

        string script = succeed
            ? $$"""
                #!/usr/bin/env bash
                printf '%s\n' "$@" > "{{argsLog}}"
                out=""; prev=""; input=""
                for a in "$@"; do
                  if [ "$prev" = "-o" ]; then out="$a"; fi
                  prev="$a"; input="$a"
                done
                cp "$input" "$out"
                echo "fake-signer: signed ok"
                exit 0
                """
            : $$"""
                #!/usr/bin/env bash
                printf '%s\n' "$@" > "{{argsLog}}"
                echo "fake-signer: simulated failure" 1>&2
                exit 1
                """;

        File.WriteAllText(binary, script);
        MakeExecutable(binary);
        return new FakeSigner(dir, binary, argsLog);
    }

    public string[] RecordedArgv() =>
        File.Exists(ArgsLogPath)
            ? File.ReadAllLines(ArgsLogPath).Where(l => l.Length > 0).ToArray()
            : [];

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
