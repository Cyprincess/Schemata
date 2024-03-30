using System.Collections.Generic;
using Schemata.Authorization.Foundation.Features;
using Schemata.Core;

namespace Schemata.Authorization.Foundation;

public class SchemataAuthorizationBuilder(SchemataBuilder builder)
{
    public SchemataBuilder Builder { get; } = builder;

    public SchemataAuthorizationBuilder UseCodeFlow() {
        AddFeature<AuthorizationCodeFlowFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseRefreshTokenFlow() {
        AddFeature<AuthorizationRefreshTokenFlowFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseClientCredentialsFlow() {
        AddFeature<AuthorizationClientCredentialsFlowFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseDeviceFlow() {
        AddFeature<AuthorizationDeviceFlowFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseIntrospection() {
        AddFeature<AuthorizationIntrospectionFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseLogout() {
        AddFeature<AuthorizationLogoutFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder UseRevocation() {
        AddFeature<AuthorizationRevocationFeature>();
        return this;
    }

    public SchemataAuthorizationBuilder AddFeature<T>()
        where T : IAuthorizationFeature, new() {
        var build = Builder.GetConfigurators().TryGet<IList<IAuthorizationFeature>>();
        build ??= _ => { };

        Builder.Configure<IList<IAuthorizationFeature>>(builder => {
            build(builder);

            builder.Add(new T());
        });

        return this;
    }
}
