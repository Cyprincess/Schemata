using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     Sends identity verification and recovery codes by phone message.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IMessageSender<in TUser>
    where TUser : SchemataUser
{
    /// <summary>
    ///     Sends a phone confirmation code to a user.
    /// </summary>
    /// <param name="user">The user receiving the code.</param>
    /// <param name="phone">The destination phone number.</param>
    /// <param name="code">The confirmation code.</param>
    Task SendConfirmationCodeAsync(TUser user, string phone, string code);

    /// <summary>
    ///     Sends a password reset code to a user.
    /// </summary>
    /// <param name="user">The user receiving the code.</param>
    /// <param name="phone">The destination phone number.</param>
    /// <param name="code">The password reset code.</param>
    Task SendPasswordResetCodeAsync(TUser user, string phone, string code);
}
