using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     Sends identity-related SMS or push messages such as confirmation and password reset codes.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IMessageSender<in TUser>
    where TUser : SchemataUser
{
    /// <summary>
    ///     Sends a confirmation code to the specified phone number.
    /// </summary>
    /// <param name="user">The user requesting confirmation.</param>
    /// <param name="phone">The target phone number.</param>
    /// <param name="code">The confirmation code.</param>
    Task SendConfirmationCodeAsync(TUser user, string phone, string code);

    /// <summary>
    ///     Sends a password reset code to the specified phone number.
    /// </summary>
    /// <param name="user">The user requesting a password reset.</param>
    /// <param name="phone">The target phone number.</param>
    /// <param name="code">The reset code.</param>
    Task SendPasswordResetCodeAsync(TUser user, string phone, string code);
}
