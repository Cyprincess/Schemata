using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Resolves an OAuth/OIDC subject identifier (`sub`)
///     back to the owning <typeparamref name="TUser" /> and produces the user's claims.
/// </summary>
internal sealed class IdentitySubjectProvider<TUser>(SchemataUserManager<TUser> manager, IClaimsProvider<TUser> claims) : ISubjectProvider
    where TUser : SchemataUser
{
    #region ISubjectProvider Members

    public async Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default) {
        var user = await manager.FindByCanonicalNameAsync(subject);
        if (user is null) {
            return [];
        }

        return await claims.GetClaimsAsync(user, ct);
    }

    public async Task<bool> ValidateAsync(string subject, CancellationToken ct = default) {
        var user = await manager.FindByCanonicalNameAsync(subject);
        return user is not null;
    }

    #endregion
}
