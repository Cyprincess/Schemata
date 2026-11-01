using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Http.Features;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchedulingBuilder{TJob,TExecution}" /> extensions that activate Scheduling HTTP resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingHttpFeature{TJob,TExecution}" />.</summary>
    public static SchedulingBuilder<TJob, TExecution> MapHttp<TJob, TExecution>(this SchedulingBuilder<TJob, TExecution> builder)
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataSchedulingHttpFeature<TJob, TExecution>>();

        return builder;
    }
}
