using Schemata.Core;
using Schemata.Flow.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseFlowHttp</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Registers Flow resources for HTTP and adds the definitions-only
    ///     controller from this assembly to MVC discovery.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="SchemataFlowHttpFeature" />
    public static SchemataBuilder UseFlowHttp(this SchemataBuilder builder) {
        builder.AddSchemataApplicationPart<SchemataFlowHttpFeature>();
        builder.AddFeature<SchemataFlowHttpFeature>();

        return builder;
    }
}
