using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultRescheduleJobHandler(IRequestHandler<ScheduleJobRequest, Unit> schedule)
    : IRequestHandler<RescheduleJobRequest, Unit>
{
    public Task<Unit> HandleAsync(RescheduleJobRequest request, CancellationToken ct = default) {
        return schedule.HandleAsync(new(request.Job, null), ct);
    }
}
