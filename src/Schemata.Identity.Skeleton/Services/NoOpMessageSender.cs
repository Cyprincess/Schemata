using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

public class NoOpMessageSender<TUser> : IMessageSender<TUser>
    where TUser : SchemataUser
{
    #region IMessageSender<TUser> Members

    public Task SendConfirmationCodeAsync(TUser user, string email, string code) {
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(TUser user, string email, string code) {
        return Task.CompletedTask;
    }

    #endregion
}
