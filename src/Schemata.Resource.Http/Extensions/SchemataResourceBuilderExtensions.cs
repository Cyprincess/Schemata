using Schemata.Resource.Foundation;
using Schemata.Resource.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling HTTP transport on the resource builder.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    ///     Enables HTTP transport for resources and returns the same builder so registration chains.
    ///     Restrict a single resource to HTTP with <c>Use&lt;T&gt;(r =&gt; r.MapHttp())</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static SchemataResourceBuilder MapHttp(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataHttpResourceFeature>();

        return builder;
    }
}
