using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

/// <summary>
///     Store augmentation that looks up a user by canonical resource name per
///     <seealso href="https://google.aip.dev/122">AIP-122: Resource names</seealso>, e.g.,
///     <c>users/chino</c>. Added in the Subject format migration (canonical name becomes the
///     OIDC <c>sub</c> claim).
/// </summary>
public interface IUserCanonicalNameStore<TUser>
    where TUser : class
{
    Task<TUser?> FindByCanonicalNameAsync(string canonicalName, CancellationToken ct);
}
