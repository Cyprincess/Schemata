using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Scheduling.Skeleton;

/// <summary>The unit of work dispatched by <see cref="IScheduler" /> on each fire.</summary>
public interface IScheduledJob
{
    /// <summary>Executes the job body for the supplied <paramref name="context" />.</summary>
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
