using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Identity;

internal static class SideportIdentityConstants
{
    internal static readonly string CookieScheme = IdentityConstants.ApplicationScheme;
    internal const string NativeIssuer = WorkspaceIdentityKey.NativePasskeyIssuer;
    internal const string AuthenticationMethodClaimType = "sideport:authentication-method";
    internal const string NativeMethod = "passkey";
    internal const string OidcMethod = "oidc";
}

internal sealed class SideportIdentityUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}

internal sealed class SideportIdentityDbContext(DbContextOptions<SideportIdentityDbContext> options)
    : IdentityDbContext<SideportIdentityUser>(options);

internal sealed class SideportIdentityClaimsPrincipalFactory(
    UserManager<SideportIdentityUser> userManager,
    IOptions<IdentityOptions> options,
    WorkspaceAccessStore workspace)
    : UserClaimsPrincipalFactory<SideportIdentityUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(SideportIdentityUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        AddOrReplace(identity, "sub", user.Id);
        AddOrReplace(identity, WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType, SideportIdentityConstants.NativeIssuer);
        AddOrReplace(identity, SideportIdentityConstants.AuthenticationMethodClaimType, SideportIdentityConstants.NativeMethod);
        AddOrReplace(identity, "name", user.DisplayName);
        if (!string.IsNullOrWhiteSpace(user.Email))
            AddOrReplace(identity, "email", user.Email);

        WorkspaceAccessDocument? document = await workspace.ReadAsync().ConfigureAwait(false);
        if (document?.Workspace.State == WorkspaceLifecycleState.Active)
        {
            AddOrReplace(
                identity,
                WorkspaceRequestPrincipalResolver.SecurityEpochClaimType,
                document.Workspace.SecurityEpoch);
        }

        return identity;
    }

    private static void AddOrReplace(ClaimsIdentity identity, string type, string value)
    {
        foreach (Claim existing in identity.FindAll(type).ToArray())
            identity.RemoveClaim(existing);
        identity.AddClaim(new Claim(type, value));
    }
}

internal static class SideportIdentityServiceCollectionExtensions
{
    internal static IdentityBuilder AddSideportNativePasskeys(
        this IServiceCollection services,
        string databasePath,
        string serverDomain)
    {
        services.AddDbContext<SideportIdentityDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Cache=Shared"));
        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = serverDomain;
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "required";
        });
        IdentityBuilder identity = services
            .AddIdentityCore<SideportIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.ProtectPersonalData = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<SideportIdentityDbContext>()
            .AddSignInManager();
        services.AddScoped<IUserClaimsPrincipalFactory<SideportIdentityUser>, SideportIdentityClaimsPrincipalFactory>();
        services.AddScoped<NativePasskeyService>();
        return identity;
    }
}
