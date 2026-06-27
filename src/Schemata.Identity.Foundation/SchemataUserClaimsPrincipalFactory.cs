using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Identity.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation;

/// <summary>
///     Issues a <see cref="Claims.Subject" /> claim whose value is the user's
///     <see cref="ICanonicalName.CanonicalName" /> (e.g. <c>"users/{uid}"</c>), aligning
///     the Identity-issued cookie / token subject with the framework-wide AIP-122
///     canonical-name contract for resource-reference fields (<c>[ResourceReference]</c>).
/// </summary>
/// <remarks>
///     Downstream OAuth / OIDC handlers see <c>sub = "users/{uid}"</c> from the first
///     sign-in step; pairwise projection runs later in the claims pipeline at the OIDC
///     wire boundary.
/// </remarks>
/// <typeparam name="TUser">The Schemata identity user entity.</typeparam>
/// <typeparam name="TRole">The Schemata identity role entity.</typeparam>
public class SchemataUserClaimsPrincipalFactory<TUser, TRole> : UserClaimsPrincipalFactory<TUser, TRole>
    where TUser : SchemataUser
    where TRole : SchemataRole
{
    /// <summary>Initializes a new <see cref="SchemataUserClaimsPrincipalFactory{TUser, TRole}" />.</summary>
    public SchemataUserClaimsPrincipalFactory(
        UserManager<TUser>        userManager,
        RoleManager<TRole>        roleManager,
        IOptions<IdentityOptions> optionsAccessor
    ) : base(userManager, roleManager, optionsAccessor) { }

    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(TUser user) {
        var identity = await base.GenerateClaimsAsync(user);

        // The base factory issues Claims.Subject from user.Id.ToString() via
        // IdentityOptions.ClaimsIdentity.UserIdClaimType. Overwrite that value with the
        // canonical name so every Schemata code path reading `sub` sees the "users/{uid}" form.
        var existing = identity.FindFirst(Options.ClaimsIdentity.UserIdClaimType);
        if (existing is not null) {
            identity.RemoveClaim(existing);
        }

        var canonical = !string.IsNullOrWhiteSpace(user.CanonicalName)
            ? user.CanonicalName!
            : $"users/{user.Uid}";
        identity.AddClaim(new Claim(Options.ClaimsIdentity.UserIdClaimType, canonical));

        return identity;
    }
}
