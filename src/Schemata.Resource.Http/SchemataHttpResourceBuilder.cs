using Schemata.Resource.Foundation;

namespace Schemata.Resource.Http;

/// <summary>
/// Fluent builder for configuring resources specifically for HTTP transport.
/// </summary>
public sealed class SchemataHttpResourceBuilder
{
    /// <summary>
    /// Initializes a new HTTP resource builder wrapping the base resource builder.
    /// </summary>
    /// <param name="builder">The base resource builder.</param>
    public SchemataHttpResourceBuilder(SchemataResourceBuilder builder) { Builder = builder; }

    /// <summary>
    /// Gets the underlying resource builder for shared configuration.
    /// </summary>
    public SchemataResourceBuilder Builder { get; }
}
