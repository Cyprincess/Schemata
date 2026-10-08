using System;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;

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
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" /> for chaining flow feature
    ///     extensions.
    /// </returns>
    /// <remarks>
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp,TAuth,TScope}" /> as the core feature.
    /// </remarks>
    public static SchemataAuthorizationBuilder<SchemataApplication, SchemataAuthorization, SchemataScope> UseAuthorization(this SchemataBuilder builder, Action<SchemataAuthorizationOptions>? configure = null) {
        return builder.UseAuthorization<SchemataApplication, SchemataAuthorization, SchemataScope>(configure);
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
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="SchemataAuthorizationOptions" />.</param>
    /// <returns>
    ///     A <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" /> for chaining flow feature
    ///     extensions.
    /// </returns>
    /// <remarks>
    ///     The installed feature maps the OIDC/OAuth authorization-server metadata and JWKS endpoints at
    ///     the paths the configured issuer derives.
    ///     Installs <see cref="SchemataAuthorizationFeature{TApp,TAuth,TScope}" /> as the core feature.
    /// </remarks>
    /// <seealso cref="SchemataAuthorizationBuilderExtensions" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseAuthorization<TApp, TAuth, TScope>(
        this SchemataBuilder                  builder,
        Action<SchemataAuthorizationOptions>? configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataAuthorizationFeature<TApp, TAuth, TScope>>();

        return new(builder.Options, builder.Configurators, builder.Services);
    }
}
