using Schemata.Core;
using Schemata.Transport.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling shared gRPC transport services.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds the shared gRPC transport feature to the Schemata builder.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The Schemata builder for chaining.</returns>
    public static SchemataBuilder UseGrpcTransport(this SchemataBuilder builder) {
        builder.AddFeature<SchemataTransportGrpcFeature>();

        return builder;
    }
}
