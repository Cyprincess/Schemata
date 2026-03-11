using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

public interface IUserDisplayNameStore<TUser>
    where TUser : class
{
    Task<string?> GetDisplayNameAsync(TUser user, CancellationToken cancellationToken);
}
