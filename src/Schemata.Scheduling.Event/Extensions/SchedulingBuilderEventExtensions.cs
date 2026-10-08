using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Scheduling.Event;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchedulingBuilder" /> extensions that configure per-job lifecycle event publishing.</summary>
public static class SchedulingBuilderEventExtensions
{
    /// <summary>Sets the publish gate for <typeparamref name="T" />.</summary>
    public static SchedulingBuilder WithEventPublishing<T>(
        this SchedulingBuilder builder,
        AdviseResult           result = AdviseResult.Continue
    )
        where T : class, IScheduledJob {
        builder.Services.Configure<SchemataSchedulingEventOptions>(options => {
            options.Jobs[typeof(T)] = new() { Result = result };
        });
        return builder;
    }
}
