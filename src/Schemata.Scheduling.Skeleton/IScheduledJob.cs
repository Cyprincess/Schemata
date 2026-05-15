using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Scheduling.Skeleton;

public interface IScheduledJob
{
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
