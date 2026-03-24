using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Store interface for retrieving a user's principal name (UPN).
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserPrincipalNameStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Gets the user principal name for the specified user.
    /// </summary>
    /// <param name="user">The user whose UPN to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The UPN, or <see langword="null"/> if not set.</returns>
    Task<string?> GetUserPrincipalNameAsync(TUser user, CancellationToken cancellationToken);
}
