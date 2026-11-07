using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;

namespace Schemata.Authorization.Identity.Services;

/// <summary>Admits password-grant users through ASP.NET Core Identity sign-in policy.</summary>
internal sealed class IdentityResourceOwnerPasswordValidator<TUser>(
    SchemataUserManager<TUser> users,
    SignInManager<TUser> sign,
    IUserClaimsPrincipalFactory<TUser> claims
) : IResourceOwnerPasswordValidator
    where TUser : SchemataUser
{
    public async Task<ClaimsPrincipal?> ValidateAsync(
        SchemataApplication application,
        string username,
        string password,
        CancellationToken ct
    ) {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByNameAsync(username);
        if (user is null) {
            return null;
        }

        var result = await sign.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!result.Succeeded || await sign.IsTwoFactorEnabledAsync(user)) {
            return null;
        }

        ct.ThrowIfCancellationRequested();
        return await claims.CreateAsync(user);
    }
}
