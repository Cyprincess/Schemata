using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton.Stores;

public interface IRoleDisplayNameStore<TRole>
    where TRole : class
{
    Task<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TRole             user,
        CancellationToken cancellationToken);
}
