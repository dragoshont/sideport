using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace Sideport.Api.WorkspaceAccess;

internal sealed record IdentityPresentationValue(string DisplayName, string? Email);

internal static class IdentityPresentation
{
    public const string FallbackDisplayName = "Sideport account";
    private const int MaxDisplayLength = 160;
    private const int MaxEmailLength = 254;

    public static IdentityPresentationValue FromClaims(string? displayName, string? email)
    {
        string safeDisplayName = NormalizeText(displayName, MaxDisplayLength)
            ?? NormalizeEmail(email)
            ?? FallbackDisplayName;
        return new IdentityPresentationValue(safeDisplayName, NormalizeEmail(email));
    }

    private static string? NormalizeEmail(string? value)
    {
        string? normalized = NormalizeText(value, MaxEmailLength);
        if (normalized is null || normalized.Any(char.IsWhiteSpace))
            return null;

        try
        {
            var parsed = new MailAddress(normalized);
            return string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized;
        try
        {
            normalized = value.Trim().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (normalized.Length is 0 || normalized.Length > maxLength)
            return null;

        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control || IsBidiControl(rune.Value))
                return null;
        }

        return normalized;
    }

    private static bool IsBidiControl(int value) =>
        value is 0x061C or 0x200E or 0x200F or
        >= 0x202A and <= 0x202E or
        >= 0x2066 and <= 0x2069;
}
