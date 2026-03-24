using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataBuilder" /> to enable authorization.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds OpenIddict-based authorization using the default entity types.
    /// </summary>
    public static SchemataAuthorizationBuilder UseAuthorization(
        this SchemataBuilder                       builder,
        Action<OpenIddictServerBuilder>?           serve     = null,
        Action<OpenIddictServerAspNetCoreBuilder>? integrate = null,
        Action<OpenIddictCoreBuilder>?             store     = null
    ) {
        return builder.UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>(serve, integrate, store);
    }

    /// <summary>
    ///     Adds OpenIddict-based authorization using custom entity types.
    /// </summary>
    public static SchemataAuthorizationBuilder UseAuthorization<TApplication, TAuthorization, TScope, TToken>(
        this SchemataBuilder                       builder,
        Action<OpenIddictServerBuilder>?           serve     = null,
        Action<OpenIddictServerAspNetCoreBuilder>? integrate = null,
        Action<OpenIddictCoreBuilder>?             store     = null
    )
        where TApplication : SchemataApplication
        where TAuthorization : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken {
        store ??= _ => { };
        builder.Configure(store);

        serve ??= _ => { };
        builder.Configure(serve);

        integrate ??= _ => { };
        builder.Configure(integrate);

        if (!builder.HasFeature<SchemataHttpsFeature>()) {
            builder.Configure<OpenIddictServerAspNetCoreBuilder>(options => {
                integrate(options);

                options.DisableTransportSecurityRequirement();
            });
        }

        builder.AddFeature<SchemataAuthorizationFeature<TApplication, TAuthorization, TScope, TToken>>();

        return new(builder.Options, builder.Configurators);
    }
}
