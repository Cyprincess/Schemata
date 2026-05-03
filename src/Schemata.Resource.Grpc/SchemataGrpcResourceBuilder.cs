using Schemata.Resource.Foundation;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Fluent builder returned by <c>MapGrpc()</c> for configuring resources
///     over gRPC transport.
/// </summary>
public sealed class SchemataGrpcResourceBuilder
{
    /// <summary>
    ///     Initializes a new gRPC resource builder wrapping a base
    ///     <see cref="SchemataResourceBuilder" />.
    /// </summary>
    /// <param name="builder">The base resource builder.</param>
    public SchemataGrpcResourceBuilder(SchemataResourceBuilder builder) { Builder = builder; }

    /// <summary>
    ///     The underlying <see cref="SchemataResourceBuilder" /> for shared configuration.
    /// </summary>
    public SchemataResourceBuilder Builder { get; }
}
