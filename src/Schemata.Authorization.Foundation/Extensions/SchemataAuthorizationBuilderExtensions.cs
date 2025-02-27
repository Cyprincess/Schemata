using System.Collections.Generic;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataAuthorizationBuilderExtensions
{
    public static SchemataAuthorizationBuilder UseCodeFlow(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationCodeFlowFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseRefreshTokenFlow(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationRefreshTokenFlowFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseClientCredentialsFlow(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationClientCredentialsFlowFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseDeviceFlow(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationDeviceFlowFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseIntrospection(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationIntrospectionFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseEndSession(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationEndSessionFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseRevocation(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationRevocationFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder UseCaching(this SchemataAuthorizationBuilder builder) {
        AddFeature<AuthorizationCachingFeature>(builder);
        return builder;
    }

    public static SchemataAuthorizationBuilder AddFeature<T>(this SchemataAuthorizationBuilder builder)
        where T : IAuthorizationFeature, new() {
        builder.Configurators.Set<IList<IAuthorizationFeature>>(configure => {
            configure.Add(new T());
        });

        return builder;
    }
}
