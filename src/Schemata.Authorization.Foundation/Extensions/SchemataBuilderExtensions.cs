using System;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataAuthorizationBuilder<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken> UseAuthorization(this SchemataBuilder builder, Action<SchemataAuthorizationOptions>? configure = null) {
        return builder.UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>(configure);
    }

    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseAuthorization<TApp, TAuth, TScope, TToken>(
        this SchemataBuilder                  builder,
        Action<SchemataAuthorizationOptions>? configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.Configure<WellKnownOptions>(wk => {
            wk.Map(Endpoints.Discovery, async (
                       DiscoveryHandler<TScope>               handler,
                       IOptions<SchemataAuthorizationOptions> options,
                       HttpContext                            _,
                       CancellationToken                      ct
                   ) => {
                       var issuer = options.Value.Issuer!;
                       var result = await handler.GetDiscoveryDocumentAsync(issuer, ct);
                       return Results.Json(result.Data);
                   });

            wk.Map(Endpoints.Jwks, (DiscoveryHandler<TScope> handler) => Results.Json(handler.GetJwks().Data));
        });

        builder.AddFeature<SchemataAuthorizationFeature<TApp, TAuth, TScope, TToken>>();

        return new(builder.Options, builder.Configurators, builder.Services);
    }
}
