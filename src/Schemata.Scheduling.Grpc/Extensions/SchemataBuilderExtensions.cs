using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Grpc.Features;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchedulingBuilder{TJob,TExecution}" /> extensions that activate Scheduling gRPC resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingGrpcFeature{TJob,TExecution}" />.</summary>
    public static SchedulingBuilder<TJob, TExecution> MapGrpc<TJob, TExecution>(this SchedulingBuilder<TJob, TExecution> builder)
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataSchedulingGrpcFeature<TJob, TExecution>>();

        return builder;
    }
}
