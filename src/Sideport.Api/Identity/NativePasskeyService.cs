using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Sideport.Api.Identity;

internal sealed record NativePasskeyProfile(string DisplayName, string Email);

internal sealed record NativePasskeyCreationResult(string CreationOptions);

internal sealed record NativePasskeyCompletionResult(
    bool Succeeded,
    string? Error = null,
    string? Message = null,
    SideportIdentityUser? User = null);

internal sealed class NativePasskeyService(
    SignInManager<SideportIdentityUser> signIn,
    UserManager<SideportIdentityUser> users)
{
    internal async Task<NativePasskeyCreationResult> CreateOptionsAsync(
        NativePasskeyProfile profile)
    {
        NativePasskeyProfile normalized = ValidateProfile(profile);
        string id = Guid.NewGuid().ToString("N");
        var entity = new PasskeyUserEntity
        {
            Id = id,
            Name = EntityName(id, normalized.Email),
            DisplayName = normalized.DisplayName,
        };
        string creationOptions = await signIn
            .MakePasskeyCreationOptionsAsync(entity)
            .ConfigureAwait(false);
        return new NativePasskeyCreationResult(creationOptions);
    }

    internal async Task<NativePasskeyCompletionResult> CompleteEnrollmentAsync(
        NativePasskeyProfile profile,
        string credentialJson)
    {
        NativePasskeyProfile normalized = ValidateProfile(profile);
        if (string.IsNullOrWhiteSpace(credentialJson) || credentialJson.Length > 128 * 1024)
            return new(false, "passkey-response-invalid", "The passkey response is invalid.");

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn
                .PerformPasskeyAttestationAsync(credentialJson)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return new(false, "passkey-ceremony-expired", "Start passkey creation again.");
        }
        if (!attestation.Succeeded || attestation.Passkey is null || attestation.UserEntity is null)
            return new(false, "passkey-attestation-failed", "Sideport could not verify this passkey.");

        PasskeyUserEntity entity = attestation.UserEntity;
        if (!Guid.TryParseExact(entity.Id, "N", out _) ||
            !string.Equals(entity.Name, EntityName(entity.Id, normalized.Email), StringComparison.Ordinal) ||
            !string.Equals(entity.DisplayName, normalized.DisplayName, StringComparison.Ordinal))
        {
            return new(false, "passkey-identity-mismatch", "The passkey identity does not match this setup request.");
        }

        var user = new SideportIdentityUser
        {
            Id = entity.Id,
            UserName = entity.Name,
            DisplayName = normalized.DisplayName,
            Email = normalized.Email,
        };
        IdentityResult created = await users.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
            return new(false, "passkey-user-create-failed", "Sideport could not create this passkey account.");

        IdentityResult passkeyAdded = await users
            .AddOrUpdatePasskeyAsync(user, attestation.Passkey)
            .ConfigureAwait(false);
        if (!passkeyAdded.Succeeded)
        {
            await users.DeleteAsync(user).ConfigureAwait(false);
            return new(false, "passkey-store-failed", "Sideport could not save this passkey.");
        }

        return new(true, User: user);
    }

    internal Task SignInUserAsync(SideportIdentityUser user) =>
        signIn.SignInAsync(
            user,
            isPersistent: false,
            SideportIdentityConstants.NativeMethod);

    internal async Task DeleteUserAsync(SideportIdentityUser user)
    {
        IdentityResult deleted = await users.DeleteAsync(user).ConfigureAwait(false);
        if (!deleted.Succeeded)
            throw new InvalidOperationException("Sideport could not roll back an incomplete passkey account.");
    }

    internal Task<string> CreateRequestOptionsAsync() =>
        signIn.MakePasskeyRequestOptionsAsync(user: null);

    internal async Task<NativePasskeyCompletionResult> CompleteSignInAsync(string credentialJson)
    {
        if (string.IsNullOrWhiteSpace(credentialJson) || credentialJson.Length > 128 * 1024)
            return new(false, "passkey-response-invalid", "The passkey response is invalid.");
        try
        {
            SignInResult result = await signIn.PasskeySignInAsync(credentialJson).ConfigureAwait(false);
            return result.Succeeded
                ? new(true)
                : new(false, "passkey-sign-in-failed", "Sideport could not verify this passkey.");
        }
        catch (InvalidOperationException)
        {
            return new(false, "passkey-ceremony-expired", "Start passkey sign-in again.");
        }
    }

    internal static NativePasskeyProfile ValidateProfile(NativePasskeyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string displayName = profile.DisplayName?.Trim() ?? string.Empty;
        string email = profile.Email?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 120)
            throw new ArgumentException("Use a name from 1 to 120 characters.", nameof(profile));
        if (email.Length is < 3 or > 254)
            throw new ArgumentException("Use a valid email address.", nameof(profile));
        try
        {
            var address = new MailAddress(email);
            if (!string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ArgumentException("Use a valid email address.", nameof(profile));
        }
        return new NativePasskeyProfile(displayName, email);
    }

    private static string EntityName(string id, string email)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(email.ToUpperInvariant()));
        return $"sideport-{id}-{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
    }
}
