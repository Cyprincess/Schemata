using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Scheduling.Event;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchedulingBuilder{TJob,TExecution}" /> extensions that configure per-job lifecycle event publishing.</summary>
public static class SchedulingBuilderEventExtensions
{
    /// <summary>Sets the publish gate for <typeparamref name="T" />.</summary>
    /// <remarks>
    ///     The method carries the builder's job and execution entity types as explicit type
    ///     arguments, so callers write <c>WithEventPublishing&lt;TJob, TExecution, T&gt;()</c>; the
    ///     closed pair is part of the public signature rather than being inferred from the builder.
    /// </remarks>
    /// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
    /// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
    /// <typeparam name="T">The scheduled job type whose events are gated.</typeparam>
    public static SchedulingBuilder<TJob, TExecution> WithEventPublishing<TJob, TExecution, T>(
        this SchedulingBuilder<TJob, TExecution> builder,
        AdviseResult                             result = AdviseResult.Continue
    )
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new()
        where T : class, IScheduledJob {
        builder.Services.Configure<SchemataSchedulingEventOptions>(options => {
            options.Jobs[typeof(T)] = new() { Result = result };
        });
        return builder;
    }
}
