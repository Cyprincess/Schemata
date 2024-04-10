using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Services;

public sealed class NoOpMailSender<TUser> : IMailSender<TUser>
    where TUser : SchemataUser
{
    #region IMailSender<TUser> Members

    public Task SendConfirmationCodeAsync(TUser user, string email, string code) {
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(TUser user, string email, string code) {
        return Task.CompletedTask;
    }

    #endregion
}
