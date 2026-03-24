using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

/// <summary>
/// Fluent builder for configuring resources specifically for gRPC transport.
/// </summary>
public sealed class SchemataGrpcResourceBuilder
{
    /// <summary>
    /// Initializes a new gRPC resource builder wrapping the base resource builder.
    /// </summary>
    /// <param name="builder">The base resource builder.</param>
    public SchemataGrpcResourceBuilder(SchemataResourceBuilder builder) { Builder = builder; }

    /// <summary>
    /// Gets the underlying resource builder for shared configuration.
    /// </summary>
    public SchemataResourceBuilder Builder { get; }
}
