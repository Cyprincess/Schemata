using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-136 <c>:run</c> handler for <see cref="SchemataJob" />. Reflects the
///     persisted <see cref="SchemataJob.JobKey" /> to dispatch through
///     <see cref="IScheduler.TriggerAsync{TJob}" />; the scheduler persists the
///     <see cref="SchemataJobExecution" /> row synchronously so the response
///     carries an addressable <c>operations/{uid}</c>.
/// </summary>
public sealed class RunJobHandler(
    IScheduler scheduler,
    IServiceProvider services,
    IScheduledJobRegistry registry)
    : IRequestHandler<RunJobRequest, Operation>
{
    private static readonly MethodInfo TriggerOpenMethod =
        typeof(IScheduler).GetMethod(nameof(IScheduler.TriggerAsync))!;

    public async Task<Operation> HandleAsync(
        RunJobRequest request,
        CancellationToken ct = default
    ) {
        var repository = services.GetRequiredService<IRepository<SchemataJob>>();
        SchemataJob? entity;
        var canonicalName = request.CanonicalName ?? string.Empty;
        using (repository.SuppressQuerySoftDelete()) {
            entity = await repository.FirstOrDefaultAsync(
                q => q.Where(r => r.Name == canonicalName || r.CanonicalName == canonicalName), ct);
        }

        if (entity is null) {
            throw new NotFoundException(message: $"Job '{canonicalName}' was not found.");
        }

        if (string.IsNullOrEmpty(entity.JobKey)) {
            throw new FailedPreconditionException(message: $"Job '{entity.CanonicalName}' cannot be run.");
        }

        var jobType = registry.Resolve(entity.JobKey);
        if (jobType is null) {
            throw new FailedPreconditionException(message: $"Job '{entity.CanonicalName}' cannot be run.");
        }

        if (services.GetService(jobType) is null) {
            throw new FailedPreconditionException(message: $"Job '{entity.CanonicalName}' cannot be run.");
        }

        var context = new JobContext {
            Job          = entity.CanonicalName ?? jobType.Name,
            Variables    = request.Variables ?? new Dictionary<string, string?>(),
            ExecutionUid = Identifiers.NewUid(),
        };

        var trigger = TriggerOpenMethod.MakeGenericMethod(jobType);

        Task<SchemataJobExecution> task;
        try {
            task = (Task<SchemataJobExecution>)trigger.Invoke(scheduler, [context, ct])!;
        } catch (TargetInvocationException tie) when (tie.InnerException is not null) {
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }

        return OperationMapper.FromExecution(await task);
    }
}
