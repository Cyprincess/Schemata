using System;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
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
