using System;
using Schemata.Core;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Features;
using Schemata.Security.Skeleton.Entities;

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

    /// <summary>Adds security services to the Schemata application against a host-supplied security entity type.</summary>
    /// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="configure">Security options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseSecurity<TSecurity>(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    ) where TSecurity : SchemataSecurity, new() {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSecurityFeature<TSecurity>>();

        return builder;
    }
}
