using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class BrowserSecurityPolicyTests
{
    [Theory]
    [InlineData("https://sideport.example", "https://sideport.example/")]
    [InlineData("https://sideport.example:8443/", "https://sideport.example:8443/")]
    [InlineData("http://localhost:6006/", "http://localhost:6006/")]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080/")]
    public void PublicOrigin_AcceptsOnlyHttpsOrLoopbackDevelopmentOrigins(
        string input,
        string expected)
    {
        Assert.Equal(expected, BrowserSecurityPolicy.ParsePublicOrigin(input).AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sideport.example")]
    [InlineData("file:///tmp/sideport")]
    [InlineData("ftp://sideport.example/")]
    [InlineData("http://sideport.example/")]
    [InlineData("https://user:secret@sideport.example/")]
    [InlineData("https://sideport.example/apps")]
    [InlineData("https://sideport.example//")]
    [InlineData("https://sideport.example/?next=/apps")]
    [InlineData("https://sideport.example/#fragment")]
    public void PublicOrigin_RejectsNonOriginOrUnsafeForms(string input)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BrowserSecurityPolicy.ParsePublicOrigin(input));

        Assert.Contains("Sideport:PublicOrigin", error.Message, StringComparison.Ordinal);
        if (input.Length > 0)
            Assert.DoesNotContain(input, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/apps", "/apps")]
    [InlineData("/apps?search=dice", "/apps?search=dice")]
    public void LocalReturnPath_AcceptsOnlyNormalizedLocalTargets(string? input, string expected)
    {
        Assert.True(BrowserSecurityPolicy.TryNormalizeLocalReturnPath(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://attacker.example/")]
    [InlineData("//attacker.example/")]
    [InlineData("/%2f%2fattacker.example/")]
    [InlineData("/%252f%252fattacker.example/")]
    [InlineData("/\\attacker.example")]
    [InlineData("/%5cattacker.example")]
    [InlineData("/%255cattacker.example")]
    [InlineData("/apps%0d%0aLocation:%20https://attacker.example")]
    [InlineData("/apps\nnext")]
    [InlineData("/invite#spinv1_secret")]
    [InlineData("/invite%23spinv1_secret")]
    [InlineData("/invite%2523spinv1_secret")]
    public void LocalReturnPath_RejectsExternalOrControlForms(string input)
    {
        Assert.False(BrowserSecurityPolicy.TryNormalizeLocalReturnPath(input, out string result));
        Assert.Equal("/", result);
    }
}
