using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Reads display names for identity users.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserDisplayNameStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Gets the display name for a user.
    /// </summary>
    /// <param name="user">The user whose display name is requested.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The user's display name.</returns>
    Task<string?> GetDisplayNameAsync(TUser user, CancellationToken ct);
}
