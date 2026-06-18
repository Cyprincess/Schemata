using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     AIP-136 <c>:run</c> handler for <see cref="SchemataJob" />. Reflects the
///     persisted <see cref="SchemataJob.JobKey" /> to dispatch through
///     <see cref="IScheduler.TriggerAsync{TJob}" />; the scheduler persists the
///     <see cref="SchemataJobExecution" /> row synchronously so the response
///     carries an addressable <c>operations/{uid}</c>.
/// </summary>
public sealed class RunJobHandler : IResourceMethodHandler<SchemataJob, RunJobRequest, Operation>
{
    private static readonly MethodInfo TriggerOpenMethod =
        typeof(IScheduler).GetMethod(nameof(IScheduler.TriggerAsync))!;

    private readonly IScheduler       _scheduler;
    private readonly IServiceProvider _services;
    private readonly IScheduledJobRegistry _registry;

    public RunJobHandler(IScheduler scheduler, IServiceProvider services, IScheduledJobRegistry registry) {
        _scheduler = scheduler;
        _services  = services;
        _registry  = registry;
    }

    #region IResourceMethodHandler<SchemataJob, RunJobRequest, Operation> Members

    public async ValueTask<Operation> InvokeAsync(
        string?           name,
        RunJobRequest     request,
        SchemataJob?      entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (string.IsNullOrEmpty(entity.JobKey)) {
            throw JobNotRunnable(entity);
        }

        var jobType = _registry.Resolve(entity.JobKey);
        if (jobType == null) {
            throw JobNotRunnable(entity);
        }

        if (_services.GetService(jobType) is null) {
            throw JobNotRunnable(entity);
        }

        var context = new JobContext {
            Job          = entity.CanonicalName ?? jobType.Name,
            Variables    = request.Variables ?? new Dictionary<string, object?>(),
            ExecutionUid = Identifiers.NewUid(),
        };

        var trigger = TriggerOpenMethod.MakeGenericMethod(jobType);

        Task<SchemataJobExecution> task;
        try {
            task = (Task<SchemataJobExecution>)trigger.Invoke(_scheduler, [context, ct])!;
        } catch (TargetInvocationException tie) when (tie.InnerException is not null) {
            // A scheduler that fails synchronously surfaces through reflection as a
            // TargetInvocationException; unwrap it so the caller sees the real failure.
            ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }

        return OperationMapper.FromExecution(await task);
    }

    #endregion

    /// <summary>
    ///     The technical reason (missing, unloadable, or unregistered job type) stays out
    ///     of the client-visible message; the persisted job row carries the specifics.
    /// </summary>
    private static FailedPreconditionException JobNotRunnable(SchemataJob entity) {
        return new(message: $"Job '{entity.CanonicalName}' cannot be run.");
    }
}
