using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>
///     No-op fallback implementation of <see cref="IMessageSender{TUser}"/> that discards all messages.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
/// <remarks>
///     Registered as the default when no real message sender is configured.
///     Replace with a real implementation to enable SMS or push delivery.
/// </remarks>
internal sealed class NoOpMessageSender<TUser> : IMessageSender<TUser>
    where TUser : SchemataUser
{
    #region IMessageSender<TUser> Members

    /// <inheritdoc />
    public Task SendConfirmationCodeAsync(TUser user, string email, string code) { return Task.CompletedTask; }

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(TUser user, string email, string code) { return Task.CompletedTask; }

    #endregion
}
