using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataAuthorizationBuilder UseAuthorization(
        this SchemataBuilder                       builder,
        Action<OpenIddictServerBuilder>?           serve     = null,
        Action<OpenIddictServerAspNetCoreBuilder>? integrate = null,
        Action<OpenIddictCoreBuilder>?             store     = null) {
        return UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>(builder, serve, integrate, store);
    }

    public static SchemataAuthorizationBuilder UseAuthorization<TApplication, TAuthorization, TScope, TToken>(
        this SchemataBuilder                       builder,
        Action<OpenIddictServerBuilder>?           serve     = null,
        Action<OpenIddictServerAspNetCoreBuilder>? integrate = null,
        Action<OpenIddictCoreBuilder>?             store     = null)
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
