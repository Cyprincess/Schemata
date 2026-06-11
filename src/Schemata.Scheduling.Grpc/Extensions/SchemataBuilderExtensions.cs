using Schemata.Core;
using Schemata.Scheduling.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate Scheduling gRPC resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingGrpcFeature" />.</summary>
    public static SchemataBuilder UseSchedulingGrpc(this SchemataBuilder builder) {
        builder.AddFeature<SchemataSchedulingGrpcFeature>();

        return builder;
    }
}
