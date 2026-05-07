using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Resolves an OAuth/OIDC subject identifier (`sub`)
///     back to the owning <typeparamref name="TUser" /> and produces the user's claims.
/// </summary>
internal sealed class IdentitySubjectProvider<TUser>(SchemataUserManager<TUser> manager) : ISubjectProvider
    where TUser : SchemataUser
{
    #region ISubjectProvider Members

    public async Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default) {
        var user = await manager.FindByIdAsync(subject);
        if (user is null) {
            return [];
        }

        var claims  = new List<Claim> {
            new(Claims.Subject, user.Uid.ToString()),
        };

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

    public async Task<bool> ValidateAsync(string subject, CancellationToken ct = default) {
        var user = await manager.FindByIdAsync(subject);
        return user is not null;
    }

    #endregion
}
