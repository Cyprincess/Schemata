using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     AIP-136 <c>:run</c> handler for <see cref="SchemataJob" />. Reflects the
///     persisted <see cref="SchemataJob.JobType" /> to dispatch through
///     <see cref="IScheduler.TriggerAsync{TJob}" />; the scheduler persists the
///     <see cref="SchemataJobExecution" /> row synchronously so the response
///     carries an addressable <c>operations/{uid}</c>.
/// </summary>
public sealed class RunJobHandler : IResourceMethodHandler<SchemataJob, RunJobRequest, SchemataJobExecution>
{
    private static readonly MethodInfo TriggerOpenMethod =
        typeof(IScheduler).GetMethod(nameof(IScheduler.TriggerAsync))!;

    private readonly IScheduler       _scheduler;
    private readonly IServiceProvider _services;

    public RunJobHandler(IScheduler scheduler, IServiceProvider services) {
        _scheduler = scheduler;
        _services  = services;
    }

    #region IResourceMethodHandler<SchemataJob, RunJobRequest, SchemataJobExecution> Members

    public async ValueTask<SchemataJobExecution> InvokeAsync(
        string?           name,
        RunJobRequest     request,
        SchemataJob       entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        if (string.IsNullOrEmpty(entity.JobType)) {
            throw new FailedPreconditionException(
                message: $"Job '{entity.CanonicalName}' has no JobType bound.");
        }

        // IScheduler.TriggerAsync<TJob> requires the concrete job type at compile time,
        // but the type is only known after loading the persisted FQN; reflect over the
        // open method instead of duplicating the dispatch path.
        var jobType = Type.GetType(entity.JobType);
        if (jobType == null) {
            throw new FailedPreconditionException(message: $"Job type '{entity.JobType}' could not be loaded.");
        }

        if (_services.GetService(jobType) is null) {
            throw new FailedPreconditionException(
                message: $"Job type '{entity.JobType}' is not registered in DI.");
        }

        var context = new JobContext {
            Job          = entity.CanonicalName ?? jobType.Name,
            Variables    = request.Variables ?? new Dictionary<string, object?>(),
            ExecutionUid = Guid.NewGuid(),
        };

        var trigger = TriggerOpenMethod.MakeGenericMethod(jobType);
        var task    = (Task<SchemataJobExecution>)trigger.Invoke(_scheduler, [context, ct])!;
        return await task;
    }

    #endregion
}
