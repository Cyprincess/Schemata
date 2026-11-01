using Schemata.Core;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate the Scheduling feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds <see cref="SchemataSchedulingFeature{TJob,TExecution}" /> for the default persisted
    ///     entities <see cref="SchemataJob" /> and <see cref="SchemataJobExecution" />.
    /// </summary>
    public static SchedulingBuilder<SchemataJob, SchemataJobExecution> UseScheduling(this SchemataBuilder builder) {
        return builder.UseScheduling<SchemataJob, SchemataJobExecution>();
    }

    /// <summary>
    ///     Adds <see cref="SchemataSchedulingFeature{TJob,TExecution}" /> and returns a
    ///     <see cref="SchedulingBuilder{TJob,TExecution}" /> for the host-defined persisted job and
    ///     execution entities.
    /// </summary>
    /// <remarks>
    ///     A host registers one job/execution pair for the application. Registering this overload
    ///     with two different pairs stages two closed scheduler registrations; resolution and
    ///     persistence then depend on registration order, so the second pair is never selected
    ///     deterministically. Call the default overload or this overload exactly once per host.
    /// </remarks>
    /// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
    /// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
    public static SchedulingBuilder<TJob, TExecution> UseScheduling<TJob, TExecution>(this SchemataBuilder builder)
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataSchedulingFeature<TJob, TExecution>>();

        return new(builder.Options, builder.Services);
    }
}
