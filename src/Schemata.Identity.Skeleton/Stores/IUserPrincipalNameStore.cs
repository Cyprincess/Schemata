using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Reads principal names for identity users.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserPrincipalNameStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Gets the principal name for a user.
    /// </summary>
    /// <param name="user">The user whose principal name is requested.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The user's principal name.</returns>
    Task<string?> GetUserPrincipalNameAsync(TUser user, CancellationToken ct);
}
