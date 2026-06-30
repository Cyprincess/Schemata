using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>MapHttp</c> extension method on <see cref="SchemataFlowBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Registers Flow resources for HTTP and adds the definitions-only
    ///     controller from this assembly to MVC discovery.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataFlowBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowHttpFeature" />
    public static SchemataFlowBuilder MapHttp(this SchemataFlowBuilder builder) {
        builder.AddFeature<SchemataFlowHttpFeature>();

        return builder;
    }
}
