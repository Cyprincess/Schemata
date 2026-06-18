using Schemata.Resource.Foundation;
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
    ///     Enables gRPC transport for resources and returns the same builder so registration chains.
    ///     Restrict a single resource to gRPC with <c>Use&lt;T&gt;(r =&gt; r.MapGrpc())</c>.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataResourceBuilder" />.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static SchemataResourceBuilder MapGrpc(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataGrpcResourceFeature>();

        return builder;
    }
}
