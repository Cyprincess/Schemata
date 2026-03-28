using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Claims;

/// <summary>
///     Generates OIDC standard claims for a user.
///     Implementations must use claim names from <see cref="Schemata.Abstractions.SchemataConstants.Claims" />.
/// </summary>
public interface IClaimsProvider<in TUser>
    where TUser : SchemataUser
{
    Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken ct = default);
}
