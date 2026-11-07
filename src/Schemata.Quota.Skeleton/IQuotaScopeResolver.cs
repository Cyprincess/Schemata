using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Quota.Skeleton;

public interface IQuotaScopeResolver
{
    ValueTask<QuotaScope> ResolveAsync(CancellationToken ct = default);
}
