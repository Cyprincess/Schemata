using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     Sends identity-related emails such as confirmation and password reset codes.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IMailSender<in TUser>
    where TUser : SchemataUser
{
    /// <summary>
    ///     Sends an email confirmation code to the specified address.
    /// </summary>
    /// <param name="user">The user requesting confirmation.</param>
    /// <param name="email">The target email address.</param>
    /// <param name="code">The confirmation code.</param>
    Task SendConfirmationCodeAsync(TUser user, string email, string code);

    /// <summary>
    ///     Sends a password reset code to the specified email address.
    /// </summary>
    /// <param name="user">The user requesting a password reset.</param>
    /// <param name="email">The target email address.</param>
    /// <param name="code">The reset code.</param>
    Task SendPasswordResetCodeAsync(TUser user, string email, string code);
}
