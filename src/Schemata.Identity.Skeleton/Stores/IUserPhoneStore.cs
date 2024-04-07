using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Skeleton.Stores;

public interface IUserPhoneStore<TUser> : IUserPhoneNumberStore<TUser>
    where TUser : class
{
    Task<TUser?> FindByPhoneAsync(string phone, CancellationToken cancellationToken);
}
