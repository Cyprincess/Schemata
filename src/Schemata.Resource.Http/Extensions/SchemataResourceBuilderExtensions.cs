using Schemata.Resource.Foundation;
using Schemata.Resource.Http;
using Schemata.Resource.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling HTTP transport on the resource builder.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    ///     Enables HTTP transport for resources and returns a builder for HTTP-specific configuration.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>An HTTP resource builder for registering HTTP-only resources.</returns>
    public static SchemataHttpResourceBuilder MapHttp(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataHttpResourceFeature>();

        return new(builder);
    }
}
