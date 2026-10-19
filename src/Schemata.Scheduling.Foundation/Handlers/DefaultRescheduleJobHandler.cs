using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation.Handlers;

internal sealed class DefaultRescheduleJobHandler<TJob, TExecution>(DefaultScheduleJobHandler<TJob, TExecution> schedule)
    : IRequestHandler<RescheduleJobRequest<TJob>, Unit>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public async Task<Unit> HandleAsync(RescheduleJobRequest<TJob> request, AdviceContext context, CancellationToken ct = default) {
        await schedule.ScheduleCoreAsync(request.Job, ct, request.Job.ScheduleVersion);
        return Unit.Value;
    }
}
