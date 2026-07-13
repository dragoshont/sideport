using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class IdentityPresentationTests
{
    [Fact]
    public void FromClaims_NormalizesPresentationWithoutChangingUnicodeText()
    {
        IdentityPresentationValue value = IdentityPresentation.FromClaims(
            "  Mara e\u0301 😊  ",
            "mara@example.test");

        Assert.Equal("Mara é 😊", value.DisplayName);
        Assert.Equal("mara@example.test", value.Email);
    }

    [Theory]
    [InlineData("Mara\nForged log line")]
    [InlineData("Mara\tInjected field")]
    [InlineData("Mara\u202Etxt.exe")]
    [InlineData("Mara\u2066spoof\u2069")]
    public void FromClaims_ControlOrBidiDisplayName_FallsBackToSafeEmail(string displayName)
    {
        IdentityPresentationValue value = IdentityPresentation.FromClaims(
            displayName,
            "mara@example.test");

        Assert.Equal("mara@example.test", value.DisplayName);
        Assert.Equal("mara@example.test", value.Email);
    }

    [Fact]
    public void FromClaims_InvalidPresentation_UsesNeutralFallback()
    {
        IdentityPresentationValue value = IdentityPresentation.FromClaims(
            new string('x', 161),
            "Mara <mara@example.test>");

        Assert.Equal(IdentityPresentation.FallbackDisplayName, value.DisplayName);
        Assert.Null(value.Email);
    }

    [Fact]
    public void FromClaims_HtmlAndFormulaPrefixRemainPlainPresentationText()
    {
        IdentityPresentationValue value = IdentityPresentation.FromClaims(
            "=1+1 <script>alert(1)</script>",
            "not an email");

        Assert.Equal("=1+1 <script>alert(1)</script>", value.DisplayName);
        Assert.Null(value.Email);
    }
}
