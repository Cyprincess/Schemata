using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

public interface IUserPrincipalNameStore<TUser>
    where TUser : class
{
    Task<string?> GetUserPrincipalNameAsync(TUser user, CancellationToken cancellationToken);
}
