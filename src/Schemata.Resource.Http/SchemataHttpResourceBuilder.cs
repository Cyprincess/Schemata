using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

/// <summary>
///     Fluent builder for configuring resources specifically for HTTP transport.
///     Wraps a <see cref="SchemataResourceBuilder" /> for shared configuration.
/// </summary>
public sealed class SchemataHttpResourceBuilder
{
    /// <summary>
    ///     Initializes a new <see cref="SchemataHttpResourceBuilder" /> wrapping the base resource builder.
    /// </summary>
    /// <param name="builder">The base <see cref="SchemataResourceBuilder" /> instance.</param>
    public SchemataHttpResourceBuilder(SchemataResourceBuilder builder) { Builder = builder; }

    /// <summary>
    ///     Gets the underlying <see cref="SchemataResourceBuilder" /> for shared configuration.
    /// </summary>
    public SchemataResourceBuilder Builder { get; }
}
