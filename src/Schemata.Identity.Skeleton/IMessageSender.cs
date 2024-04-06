using System.Threading.Tasks;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton;

public interface IMessageSender<in TUser>
    where TUser : SchemataUser
{
    Task SendConfirmationCodeAsync(TUser user, string phone, string code);

    Task SendPasswordResetCodeAsync(TUser user, string phone, string code);
}
