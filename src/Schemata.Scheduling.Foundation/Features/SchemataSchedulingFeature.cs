using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Features;

/// <summary>
///     Marks the Scheduling subsystem as installed. The persisted job and execution entity types
///     are chosen by <see cref="SchemataSchedulingFeature{TJob,TExecution}" />; this marker only
///     carries the feature ordering contract, so a bridge depending on it never forces the default
///     closed entity types.
/// </summary>
public sealed class SchemataSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 80_000_000;

    public override int Priority => DefaultPriority;
}

/// <summary>
///     Registers the in-memory <see cref="IScheduler{TJob,TExecution}" />, the execution dispatch
///     loop, the operation service, and the request handlers closed on the job and execution entity
///     types <typeparamref name="TJob" /> and <typeparamref name="TExecution" />.
/// </summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
[DependsOn<SchemataSchedulingFeature>]
public sealed class SchemataSchedulingFeature<TJob, TExecution> : FeatureBase
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    public override int Priority => SchemataSchedulingFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataScheduling<TJob, TExecution>();
}
