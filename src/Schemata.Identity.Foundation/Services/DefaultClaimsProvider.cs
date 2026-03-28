using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Services;

public class DefaultClaimsProvider<TUser>(SchemataUserManager<TUser> manager) : IClaimsProvider<TUser>
    where TUser : SchemataUser
{
    #region IClaimsProvider<TUser> Members

    /// <inheritdoc />
    public async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken ct = default) {
        var subject = user.CanonicalName ?? await manager.GetUserIdAsync(user);
        var claims  = new List<Claim> { new(Claims.Subject, subject) };

        var username = await manager.GetUserPrincipalNameAsync(user);
        if (!string.IsNullOrWhiteSpace(username)) {
            claims.Add(new(Claims.PreferredUsername, username));
        }

        var email = await manager.GetEmailAsync(user);
        if (!string.IsNullOrWhiteSpace(email)) {
            claims.Add(new(Claims.Email, email));
            claims.Add(new(Claims.EmailVerified, (await manager.IsEmailConfirmedAsync(user)).ToString().ToLowerInvariant()));
        }

        var phone = await manager.GetPhoneNumberAsync(user);
        if (!string.IsNullOrWhiteSpace(phone)) {
            claims.Add(new(Claims.PhoneNumber, phone));
            claims.Add(new(Claims.PhoneNumberVerified, (await manager.IsPhoneNumberConfirmedAsync(user)).ToString().ToLowerInvariant()));
        }

        var display = await manager.GetDisplayNameAsync(user);
        if (!string.IsNullOrWhiteSpace(display)) {
            claims.Add(new(Claims.Nickname, display));
        }

        foreach (var role in await manager.GetRolesAsync(user)) {
            claims.Add(new(Claims.Role, role));
        }

        return claims;
    }

    #endregion
}
