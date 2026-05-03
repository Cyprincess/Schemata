using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc;
using Schemata.Resource.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling gRPC transport on
///     <see cref="SchemataResourceBuilder" />.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    ///     Enables gRPC transport for resources and returns a
    ///     <see cref="SchemataGrpcResourceBuilder" /> for gRPC-specific configuration.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataResourceBuilder" />.</param>
    /// <returns>A <see cref="SchemataGrpcResourceBuilder" /> for registering gRPC resources.</returns>
    public static SchemataGrpcResourceBuilder MapGrpc(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataGrpcResourceFeature>();

        return new(builder);
    }
}
