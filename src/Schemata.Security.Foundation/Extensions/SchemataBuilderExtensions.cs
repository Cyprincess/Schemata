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
    /// <summary>Adds security services over the default security and token entities to the Schemata application.</summary>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="configure">Security options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseSecurity(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    ) {
        return builder.UseSecurity<SchemataSecurity, SchemataToken>(configure);
    }

    /// <summary>
    ///     Adds security services over a host-supplied security entity type and the default
    ///     <see cref="SchemataToken" /> token entity to the Schemata application.
    /// </summary>
    /// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="configure">Security options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseSecurity<TSecurity>(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    ) where TSecurity : SchemataSecurity, new() {
        return builder.UseSecurity<TSecurity, SchemataToken>(configure);
    }

    /// <summary>
    ///     Adds security services over host-supplied security and token entity types to the
    ///     Schemata application.
    /// </summary>
    /// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
    /// <typeparam name="TToken">Concrete token entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="configure">Security options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseSecurity<TSecurity, TToken>(
        this SchemataBuilder             builder,
        Action<SchemataSecurityOptions>? configure = null
    )
        where TSecurity : SchemataSecurity, new()
        where TToken : SchemataToken, new() {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataSecurityFeature<TSecurity, TToken>>();

        return builder;
    }
}
