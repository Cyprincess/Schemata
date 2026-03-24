using System.Collections.Generic;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataAuthorizationBuilder" /> to enable authorization features.
/// </summary>
public static class SchemataAuthorizationBuilderExtensions
{
    /// <summary>Enables the OAuth 2.0 Authorization Code flow with PKCE.</summary>
    public static SchemataAuthorizationBuilder UseCodeFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationCodeFlowFeature>();
        return builder;
    }

    /// <summary>Enables the OAuth 2.0 Refresh Token flow.</summary>
    public static SchemataAuthorizationBuilder UseRefreshTokenFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationRefreshTokenFlowFeature>();
        return builder;
    }

    /// <summary>Enables the OAuth 2.0 Client Credentials flow.</summary>
    public static SchemataAuthorizationBuilder UseClientCredentialsFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationClientCredentialsFlowFeature>();
        return builder;
    }

    /// <summary>Enables the OAuth 2.0 Device Authorization flow.</summary>
    public static SchemataAuthorizationBuilder UseDeviceFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationDeviceFlowFeature>();
        return builder;
    }

    /// <summary>Enables the OAuth 2.0 Token Introspection endpoint.</summary>
    public static SchemataAuthorizationBuilder UseIntrospection(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationIntrospectionFeature>();
        return builder;
    }

    /// <summary>Enables the OpenID Connect End Session (logout) endpoint.</summary>
    public static SchemataAuthorizationBuilder UseEndSession(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationEndSessionFeature>();
        return builder;
    }

    /// <summary>Enables the OAuth 2.0 Token Revocation endpoint.</summary>
    public static SchemataAuthorizationBuilder UseRevocation(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationRevocationFeature>();
        return builder;
    }

    /// <summary>Enables request caching for authorization and end-session endpoints.</summary>
    public static SchemataAuthorizationBuilder UseCaching(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationCachingFeature>();
        return builder;
    }

    /// <summary>Registers a custom <see cref="IAuthorizationFeature" /> implementation.</summary>
    /// <typeparam name="T">The feature type to add.</typeparam>
    public static SchemataAuthorizationBuilder AddFeature<T>(this SchemataAuthorizationBuilder builder)
        where T : IAuthorizationFeature, new() {
        builder.Configurators.Set<IList<IAuthorizationFeature>>(configure => { configure.Add(new T()); });

        return builder;
    }
}
