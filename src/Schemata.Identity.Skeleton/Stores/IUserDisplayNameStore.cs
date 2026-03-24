using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Store interface for retrieving a user's display name.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserDisplayNameStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Gets the display name for the specified user.
    /// </summary>
    /// <param name="user">The user whose display name to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The display name, or <see langword="null"/> if not set.</returns>
    Task<string?> GetDisplayNameAsync(TUser user, CancellationToken cancellationToken);
}
