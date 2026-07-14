using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sideport.Api.Tests;

public sealed class PublicOriginStartupTests
{
    [Fact]
    public void Startup_RejectsUnknownIdentityModeBeforeServingRequests()
    {
        string state = Path.Combine(
            Path.GetTempPath(),
            "sideport-identity-mode-tests",
            Guid.NewGuid().ToString("N"));
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-IDENTITY-MODE-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(PublicOriginStartupTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", state);
                builder.UseSetting("Sideport:PublicOrigin", "https://sideport.test/");
                builder.UseSetting("Sideport:Identity:Mode", "magic-link");
            });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            ExceptionChain(error),
            item => item.Message.Contains("Sideport:Identity:Mode", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("file:///tmp/sideport")]
    [InlineData("http://sideport.example/")]
    [InlineData("https://sideport.example/apps")]
    public void Startup_RejectsUnsafePublicOriginBeforeServingRequests(string publicOrigin)
    {
        string state = Path.Combine(
            Path.GetTempPath(),
            "sideport-public-origin-tests",
            Guid.NewGuid().ToString("N"));
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-PUBLIC-ORIGIN-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(PublicOriginStartupTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", state);
                builder.UseSetting("Sideport:PublicOrigin", publicOrigin);
            });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            ExceptionChain(error),
            item => item.Message.Contains("Sideport:PublicOrigin", StringComparison.Ordinal));
    }

    private static IEnumerable<Exception> ExceptionChain(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            yield return current;
    }
}
