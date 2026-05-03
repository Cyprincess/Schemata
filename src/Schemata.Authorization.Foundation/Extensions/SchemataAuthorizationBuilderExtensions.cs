using System;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods on <see cref="SchemataAuthorizationBuilder{TApp,TAuth,TScope,TToken}" /> for registering
///     OAuth 2.0 / OIDC flow features,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     and <seealso href="https://openid.net/specs/openid-connect-core-1_0.html">OpenID Connect Core 1.0</seealso>.
/// </summary>
public static class SchemataAuthorizationBuilderExtensions
{
    /// <summary>
    ///     Enables the OAuth 2.0 Authorization Code flow,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1: Authorization Code Grant
    ///     </seealso>
    ///     and
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html">
    ///         RFC 7636: Proof Key for Code Exchange by OAuth Public
    ///         Clients
    ///     </seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="AuthorizationCodeFlowFeature{TApp,TAuth,TScope,TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseCodeFlow<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder,
        Action<CodeFlowOptions>?                                       configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        if (configure is not null) {
            builder.Configurators.Set(configure);
        }

        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<InteractionFeature>();
        builder.AddFlowFeature<AuthorizationCodeFlowFeature<TApp, TAuth, TScope, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables the OAuth 2.0 Client Credentials flow,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.4">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.4: Client Credentials Grant
    ///     </seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="ClientCredentialsFlowFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseClientCredentialsFlow<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<ClientCredentialsFlowFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the OAuth 2.0 Refresh Token flow,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-6">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §6: Refreshing an Access Token
    ///     </seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="RefreshTokenFlowFeature{TApp, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseRefreshTokenFlow<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder,
        Action<RefreshTokenFlowOptions>?                               configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        if (configure is not null) {
            builder.Configurators.Set(configure);
        }

        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<RefreshTokenFlowFeature<TApp, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables the OAuth 2.0 Device Authorization Grant,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="DeviceFlowFeature{TApp, TAuth, TScope, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseDeviceFlow<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<InteractionFeature>();
        builder.AddFlowFeature<DeviceFlowFeature<TApp, TAuth, TScope, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Exchange flow,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="TokenExchangeFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseTokenExchange<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<TokenExchangeFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Introspection endpoint,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="IntrospectionFeature{TApp, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseIntrospection<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<IntrospectionFeature<TApp, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Revocation endpoint,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="RevocationFeature{TApp, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseRevocation<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<RevocationFeature<TApp, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables the OIDC UserInfo endpoint,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
    ///         OpenID Connect Core 1.0 §5.3:
    ///         UserInfo Endpoint
    ///     </seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="UserInfoFeature" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseUserInfo<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<UserInfoFeature>();
        return builder;
    }

    /// <summary>
    ///     Enables OIDC Front-Channel Logout,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-frontchannel-1_0.html">
    ///         OpenID Connect Front-Channel Logout
    ///         1.0
    ///     </seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="FrontChannelLogoutFeature{TApp, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseFrontChannelLogout<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<FrontChannelLogoutFeature<TApp, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables OIDC Back-Channel Logout,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="BackChannelLogoutFeature{TApp, TToken}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseBackChannelLogout<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<BackChannelLogoutFeature<TApp, TToken>>();
        return builder;
    }

    /// <summary>
    ///     Enables OIDC RP-Initiated Logout,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="EndSessionFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> UseEndSession<TApp, TAuth, TScope, TToken>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope, TToken> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope
        where TToken : SchemataToken, new() {
        builder.AddFlowFeature<EndSessionFeature<TApp>>();
        return builder;
    }
}
