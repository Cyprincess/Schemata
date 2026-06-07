using Schemata.Core;
using Schemata.Transport.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><c>UseGrpcTransport</c> extension on <see cref="SchemataBuilder" />.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Enables <see cref="SchemataTransportGrpcFeature" />.</summary>
    public static SchemataBuilder UseGrpcTransport(this SchemataBuilder builder) {
        builder.AddFeature<SchemataTransportGrpcFeature>();

        return builder;
    }
}
