using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-136 <c>:run</c> handler for the host-defined persisted job entity. Reflects the
///     persisted <see cref="SchemataJob.JobKey" /> to dispatch through
///     <see cref="IScheduler.TriggerAsync{TJob}" />; the scheduler persists the execution row
///     synchronously so the response carries the persisted operation's canonical name.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
public sealed class RunJobHandler<TJob>(
    IScheduler scheduler,
    IServiceProvider services,
    IScheduledJobRegistry registry)
    : IRequestHandler<RunJobRequest, Operation>
    where TJob : SchemataJob, new()
{
    private static readonly MethodInfo TriggerOpenMethod =
        typeof(IScheduler).GetMethod(nameof(IScheduler.TriggerAsync))!;

    public async Task<Operation> HandleAsync(
        RunJobRequest     request,
        AdviceContext     advice,
        CancellationToken ct = default
    ) {
        var repository = services.GetRequiredService<IRepository<TJob>>();
        var canonicalName = request.CanonicalName ?? string.Empty;
        var entity = await repository.FirstOrDefaultAsync(
            q => q.Where(r => r.Name == canonicalName || r.CanonicalName == canonicalName), ct,
            new QueryOptions(IgnoreSoftDelete: true));

        if (entity is null) {
            throw new NotFoundException(
                SchemataResources.JOB_NOT_FOUND,
                new Dictionary<string, string?> { ["name"] = canonicalName });
        }

        if (string.IsNullOrEmpty(entity.JobKey) || string.IsNullOrWhiteSpace(entity.CanonicalName)) {
            throw new FailedPreconditionException(
                SchemataResources.JOB_NOT_RUNNABLE,
                new Dictionary<string, string?> { ["name"] = entity.CanonicalName });
        }

        var jobType = registry.Resolve(entity.JobKey);
        if (jobType is null) {
            throw new FailedPreconditionException(
                SchemataResources.JOB_NOT_RUNNABLE,
                new Dictionary<string, string?> { ["name"] = entity.CanonicalName });
        }

        if (services.GetService(jobType) is null) {
            throw new FailedPreconditionException(
                SchemataResources.JOB_NOT_RUNNABLE,
                new Dictionary<string, string?> { ["name"] = entity.CanonicalName });
        }

        // A manual run replays the stored job's own arguments and variables; the caller's
        // request variables override colliding keys for this fire only. The persisted message
        // context is adopted by the trigger handler, so the dispatcher's context restore keeps
        // running on the original execution-row path.
        var context = new JobContext {
            Job          = entity.CanonicalName,
            Variables    = MergeVariables(entity.Variables, request.Variables),
            ArgsJson     = entity.ArgsJson,
            ExecutionUid = Guid.NewGuid(),
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

    private static Dictionary<string, string?> MergeVariables(
        Dictionary<string, string?>? stored,
        Dictionary<string, string?>? overrides
    ) {
        var merged = new Dictionary<string, string?>();
        if (stored is not null) {
            foreach (var (key, value) in stored) {
                merged[key] = value;
            }
        }

        if (overrides is not null) {
            foreach (var (key, value) in overrides) {
                merged[key] = value;
            }
        }

        return merged;
    }
}
