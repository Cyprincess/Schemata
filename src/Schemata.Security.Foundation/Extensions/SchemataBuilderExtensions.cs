using Schemata.Core;
using Schemata.Security.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for configuring Schemata security features on <see cref="SchemataBuilder"/>.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds the Schemata security feature, registering default access and entitlement providers.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseSecurity(this SchemataBuilder builder) {
        builder.AddFeature<SchemataSecurityFeature>();

        return builder;
    }
}
