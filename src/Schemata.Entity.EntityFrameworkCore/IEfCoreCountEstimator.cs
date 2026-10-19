using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Estimates the logical result count of a query after repository build-query advisors have run.
/// </summary>
/// <remarks>
///     Return null for unsupported queries. Implementations must preserve query visibility and propagate failures
///     and cancellation. The context belongs to the repository and must not be disposed or used concurrently.
/// </remarks>
public interface IEfCoreCountEstimator<TContext> where TContext : DbContext
{
    ValueTask<long?> EstimateAsync<TResult>(TContext context, IQueryable<TResult> query, CancellationToken ct = default);
}
