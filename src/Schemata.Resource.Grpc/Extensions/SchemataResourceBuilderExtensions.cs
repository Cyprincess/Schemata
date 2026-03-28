using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc;
using Schemata.Resource.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling gRPC transport on the resource builder.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    ///     Enables gRPC transport for resources and returns a builder for gRPC-specific configuration.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>A gRPC resource builder for registering gRPC-only resources.</returns>
    public static SchemataGrpcResourceBuilder MapGrpc(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataGrpcResourceFeature>();

        return new(builder);
    }
}
