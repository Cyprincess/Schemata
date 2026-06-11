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
    ///     Registers Flow resources for gRPC and maps the definitions-only gRPC service.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowGrpcFeature" />
    public static SchemataBuilder UseFlowGrpc(this SchemataBuilder builder) {
        builder.AddFeature<SchemataFlowGrpcFeature>();

        return builder;
    }
}
