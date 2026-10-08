using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultRescheduleJobHandler(DefaultScheduleJobHandler schedule)
    : IRequestHandler<RescheduleJobRequest, Unit>
{
    public async Task<Unit> HandleAsync(RescheduleJobRequest request, CancellationToken ct = default) {
        await schedule.ScheduleCoreAsync(request.Job, ct, request.Job.ScheduleVersion);
        return Unit.Value;
    }
}
