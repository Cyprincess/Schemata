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

/// <summary>
///     Extension methods on <see cref="SchemataBuilder" /> for registering the Schemata Authorization feature,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     and <seealso href="https://openid.net/specs/openid-connect-core-1_0.html">OpenID Connect Core 1.0</seealso>.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds the Schemata Authorization server with default entity types to the application.
    /// </summary>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SchemataAuthorizationOptions" />.</param>
    /// <returns>
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" /> for chaining flow feature
    ///     extensions.
    /// </returns>
    /// <remarks>
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp,TAuth,TScope,TToken}" /> as the core feature.
    /// </remarks>
    /// <seealso cref="SchemataAuthorizationFeature{TApp,TAuth,TScope,TToken}" />
    public static SchemataAuthorizationBuilder<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken> UseAuthorization(this SchemataBuilder builder, Action<SchemataAuthorizationOptions>? configure = null) {
        return builder.UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataToken>(configure);
    }

    /// <summary>
    ///     Adds the Schemata Authorization server with custom entity types to the application,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
    ///     and <seealso href="https://openid.net/specs/openid-connect-core-1_0.html">OpenID Connect Core 1.0</seealso>.
    /// </summary>
    /// <typeparam name="TApp">The application entity type.</typeparam>
    /// <typeparam name="TAuth">The authorization entity type.</typeparam>
    /// <typeparam name="TScope">The scope entity type.</typeparam>
    /// <typeparam name="TToken">The token entity type.</typeparam>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SchemataAuthorizationOptions" />.</param>
    /// <returns>
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope, TToken}" /> for chaining flow feature
    ///     extensions.
    /// </returns>
    /// <remarks>
    ///     Maps the OIDC discovery endpoint and JWKS endpoint to the well-known pipeline.
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp, TAuth, TScope, TToken}" /> as the core feature.
    /// </remarks>
    /// <seealso cref="SchemataAuthorizationFeature{TApp, TAuth, TScope, TToken}" />
    /// <seealso cref="SchemataAuthorizationBuilderExtensions" />
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
