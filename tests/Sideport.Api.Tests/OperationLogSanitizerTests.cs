using Microsoft.Extensions.Logging;
using Sideport.Api.Diagnostics;

namespace Sideport.Api.Tests;

public sealed class OperationLogSanitizerTests
{
    [Fact]
    public void Add_RedactsAuthorityIdentityDeviceAppleRepositoryAndPaths()
    {
        var store = new OperationLogStore(5);
        const string raw = "Authorization: Bearer top-secret spinv1_invitation_abc_secret " +
            "mara@example.test oidc:https://id.example:subject " +
            "00008110-0012345678901234 teamId=ABCDE12345 " +
            "0123456789abcdef0123456789abcdef01234567 " +
            "minted signing certificate serial A1B2C3D4E5F60718 " +
            "repository=family/private-app /var/lib/sideport/private/app.ipa";

        store.Add(
            LogLevel.Error,
            "Sideport.Test",
            new EventId(7),
            raw,
            new InvalidOperationException("password=hunter2 C:\\Users\\Mara\\secret.txt"));

        OperationLogEntry entry = Assert.Single(store.Read());
        Assert.DoesNotContain("top-secret", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("spinv1_", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mara@example.test", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("subject", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("00008110", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("A1B2C3D4E5F60718", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDE12345", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("family/private-app", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/lib", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", entry.ExceptionMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users", entry.ExceptionMessage, StringComparison.Ordinal);
        Assert.Equal(nameof(InvalidOperationException), entry.ExceptionType);
    }

    [Fact]
    public void Add_FlattensAndBoundsUntrustedMessages()
    {
        var store = new OperationLogStore(5);
        string raw = "first\r\nforged\t" + new string('x', 5_000);

        store.Add(LogLevel.Warning, "Sideport.Test", default, raw, exception: null);

        string message = Assert.Single(store.Read()).Message;
        Assert.DoesNotContain('\r', message);
        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\t', message);
        Assert.True(message.Length <= 4_097);
    }
}
