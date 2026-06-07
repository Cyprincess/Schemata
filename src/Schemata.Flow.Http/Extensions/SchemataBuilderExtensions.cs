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
    ///     Exposes Flow process management endpoints over HTTP via the
    ///     <c>ProcessController</c> in this assembly. Enables
    ///     <see cref="SchemataFlowHttpFeature" /> and registers a
    ///     <c>SchemataExtensionPart&lt;SchemataFlowHttpFeature&gt;</c> with MVC so
    ///     that controllers from this assembly are discovered.
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
