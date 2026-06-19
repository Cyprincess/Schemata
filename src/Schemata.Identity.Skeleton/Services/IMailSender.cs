using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     Sends identity verification and recovery codes by email.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IMailSender<in TUser>
    where TUser : SchemataUser
{
    /// <summary>
    ///     Sends an email confirmation code to a user.
    /// </summary>
    /// <param name="user">The user receiving the code.</param>
    /// <param name="email">The destination email address.</param>
    /// <param name="code">The confirmation code.</param>
    Task SendConfirmationCodeAsync(TUser user, string email, string code);

    /// <summary>
    ///     Sends a password reset code to a user.
    /// </summary>
    /// <param name="user">The user receiving the code.</param>
    /// <param name="email">The destination email address.</param>
    /// <param name="code">The password reset code.</param>
    Task SendPasswordResetCodeAsync(TUser user, string email, string code);
}
