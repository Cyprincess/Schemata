using Schemata.Core;
using Schemata.Flow.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseFlowGrpc</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Exposes Flow process management as a code-first gRPC service by enabling
    ///     <see cref="SchemataFlowGrpcFeature" />, which explicitly maps
    ///     <c>ProcessService</c> via
    ///     <see cref="GrpcEndpointRouteBuilderExtensions.MapGrpcService{TService}" />.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowGrpcFeature" />
    public static SchemataBuilder UseFlowGrpc(this SchemataBuilder builder) {
        builder.AddFeature<SchemataFlowGrpcFeature>();

        return builder;
    }
}
