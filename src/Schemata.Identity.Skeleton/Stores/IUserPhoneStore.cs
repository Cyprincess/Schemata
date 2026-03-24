using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Extends <see cref="IUserPhoneNumberStore{TUser}"/> with phone number lookup capability.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserPhoneStore<TUser> : IUserPhoneNumberStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Finds a user by their phone number.
    /// </summary>
    /// <param name="phone">The phone number to search for.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The user if found; otherwise, <see langword="null"/>.</returns>
    Task<TUser?> FindByPhoneAsync(string phone, CancellationToken cancellationToken);
}
