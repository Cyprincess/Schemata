using Schemata.Core;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate Scheduling gRPC resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingGrpcFeature" />.</summary>
    public static SchedulingBuilder MapGrpc(this SchedulingBuilder builder) {
        builder.AddFeature<SchemataSchedulingGrpcFeature>();

        return builder;
    }
}
