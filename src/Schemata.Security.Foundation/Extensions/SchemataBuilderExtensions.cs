using System;
using Schemata.Core;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures the Schemata security feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds security services to the Schemata application.</summary>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="configure">Security options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseSecurity(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    ) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSecurityFeature>();

        return builder;
    }
}
