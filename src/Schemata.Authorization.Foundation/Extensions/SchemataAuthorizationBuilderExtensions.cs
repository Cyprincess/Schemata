using System.Collections.Generic;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder UseCodeFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationCodeFlowFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseRefreshTokenFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationRefreshTokenFlowFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseClientCredentialsFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationClientCredentialsFlowFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseDeviceFlow(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationDeviceFlowFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseIntrospection(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationIntrospectionFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseEndSession(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationEndSessionFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseRevocation(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationRevocationFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder UseCaching(this SchemataAuthorizationBuilder builder) {
        builder.AddFeature<AuthorizationCachingFeature>();
        return builder;
    }

    public static SchemataAuthorizationBuilder AddFeature<T>(this SchemataAuthorizationBuilder builder)
        where T : IAuthorizationFeature, new() {
        builder.Configurators.Set<IList<IAuthorizationFeature>>(configure => { configure.Add(new T()); });

        return builder;
    }
}
