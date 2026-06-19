using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Extends ASP.NET Identity phone-number storage with phone lookup.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IUserPhoneStore<TUser> : IUserPhoneNumberStore<TUser>
    where TUser : class
{
    /// <summary>
    ///     Finds a user by phone number.
    /// </summary>
    /// <param name="phone">The phone number to match.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The matching user, or <see langword="null" /> when the phone number is unknown.</returns>
    Task<TUser?> FindByPhoneAsync(string phone, CancellationToken ct);
}
