using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Quota.Skeleton;

public interface IQuotaSubjectResolver
{
    ValueTask<QuotaSubject> ResolveAsync(ClaimsPrincipal principal, string? requestedSubject, CancellationToken ct = default);
}
