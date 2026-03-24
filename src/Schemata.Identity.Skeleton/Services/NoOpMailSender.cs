using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     No-op fallback implementation of <see cref="IMailSender{TUser}"/> that discards all messages.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
/// <remarks>
///     Registered as the default when no real mail sender is configured.
///     Replace with a real implementation to enable email delivery.
/// </remarks>
internal sealed class NoOpMailSender<TUser> : IMailSender<TUser>
    where TUser : SchemataUser
{
    #region IMailSender<TUser> Members

    /// <inheritdoc />
    public Task SendConfirmationCodeAsync(TUser user, string email, string code) { return Task.CompletedTask; }

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(TUser user, string email, string code) { return Task.CompletedTask; }

    #endregion
}
