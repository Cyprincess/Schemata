using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Finds users by canonical resource name.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserCanonicalNameStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Finds a user by canonical resource name.
    /// </summary>
    /// <param name="canonicalName">The canonical resource name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The matching user, or <see langword="null" /> when the canonical name is unknown.</returns>
    Task<TUser?> FindByCanonicalNameAsync(string canonicalName, CancellationToken ct);
}
