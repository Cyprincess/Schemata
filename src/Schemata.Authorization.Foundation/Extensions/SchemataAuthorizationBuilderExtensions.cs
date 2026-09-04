using System;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods on <see cref="SchemataAuthorizationBuilder{TApp,TAuth,TScope}" /> for registering
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseCodeFlow<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder,
        Action<CodeFlowOptions>?                                       configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope {
        if (configure is not null) {
            builder.Configurators.Set(configure);
        }

        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<InteractionFeature>();
        builder.AddFlowFeature<AuthorizationCodeFlowFeature<TApp, TAuth, TScope>>();
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseClientCredentialsFlow<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseRefreshTokenFlow<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder,
        Action<RefreshTokenFlowOptions>?                               configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        if (configure is not null) {
            builder.Configurators.Set(configure);
        }

        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<RefreshTokenFlowFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the OAuth 2.0 Device Authorization Grant,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseDeviceFlow<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization, new()
        where TScope : SchemataScope {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<InteractionFeature>();
        builder.AddFlowFeature<DeviceFlowFeature<TApp, TAuth, TScope>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Exchange flow,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="TokenExchangeFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseTokenExchange<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<TokenExchangeFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the RFC 7523 jwt-bearer grant, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html">RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client Authentication and Authorization Grants</seealso>
    ///     . The grant stays unusable until
    ///     <see cref="SchemataAuthorizationOptions.JwtBearerTrustedIssuers" /> holds at least one
    ///     trusted issuer.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="JwtBearerGrantFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseJwtBearerGrant<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<TokenFeature>();
        builder.AddFlowFeature<JwtBearerGrantFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Introspection endpoint,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <summary>
    ///     Enables rich authorization requests, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html">RFC 9396: OAuth 2.0 Rich Authorization Requests</seealso>
    ///     .
    /// </summary>
    /// <remarks>
    ///     Detail-type descriptors are host-registered <c>IAuthorizationDetailTypeDescriptor</c> services.
    ///     Without the feature, authorize requests carrying <c>authorization_details</c> are ignored
    ///     (RFC 6749 §3.1 unrecognized-parameter posture) and reach no grant.
    /// </remarks>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseRichAuthorizationRequests<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<RichAuthorizationFeature<TApp>>();
        return builder;
    }

    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseIntrospection<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<IntrospectionFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the dynamic client registration flow, per
    ///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">OpenID Connect Dynamic Client Registration 1.0</seealso>
    ///     .
    /// </summary>
    /// <remarks>
    ///     Registration requests are denied with 401 until the host registers an
    ///     <c>IInitialAccessTokenValidator</c>; anonymous registration is never accepted.
    /// </remarks>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseDynamicClientRegistration<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication, new()
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<DynamicRegistrationFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables the Token Revocation endpoint,
    ///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html">RFC 7009: OAuth 2.0 Token Revocation</seealso>.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseRevocation<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<RevocationFeature<TApp>>();
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseUserInfo<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<UserInfoFeature>();
        return builder;
    }

    /// <summary>
    ///     Enables pairwise subject identifiers, per
    ///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
    ///         OpenID Connect Core 1.0 §8: Subject Identifier Types
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>
    ///     Without the feature every subject is public; <see cref="SchemataAuthorizationOptions.SubjectType" />
    ///     (global default or per application) and <see cref="SchemataAuthorizationOptions.PairwiseSalt" />
    ///     configure the feature but never enable it.
    /// </remarks>
    /// <returns>The builder for chaining.</returns>
    /// <seealso cref="PairwiseFeature{TApp}" />
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UsePairwiseSubjects<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<PairwiseFeature<TApp>>();
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseFrontChannelLogout<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<FrontChannelLogoutFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Enables OIDC Back-Channel Logout,
    ///     per
    ///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>
    ///     .
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseBackChannelLogout<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<BackChannelLogoutFeature<TApp>>();
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
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseEndSession<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        builder.AddFlowFeature<EndSessionFeature<TApp>>();
        return builder;
    }

    /// <summary>
    ///     Offers OAuth 2.0 Demonstrating Proof-of-Possession (DPoP),
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html">RFC 9449: OAuth 2.0 Demonstrating Proof of Possession (DPoP)</seealso>
    ///     : the <c>DPoP</c> authentication scheme, proof validation at the token endpoint,
    ///     <c>dpop_jkt</c> binding at the authorize endpoint, discovery metadata, host-wide
    ///     proof enforcement, and proof/nonce configuration.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public static SchemataAuthorizationBuilder<TApp, TAuth, TScope> UseDemonstratingProofOfPossession<TApp, TAuth, TScope>(
        this SchemataAuthorizationBuilder<TApp, TAuth, TScope> builder,
        Action<DPopOptions>?                                           configure = null
    )
        where TApp : SchemataApplication
        where TAuth : SchemataAuthorization
        where TScope : SchemataScope {
        if (configure is not null) {
            builder.Configurators.Set(configure);
        }

        builder.AddFlowFeature<DPopFlowFeature<TApp>>();
        return builder;
    }
}
