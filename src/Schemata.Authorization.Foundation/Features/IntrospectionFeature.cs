using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public sealed class IntrospectionFeature<TApp, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IAuthorizationFlowFeature Members

    public int Order => 4_000;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<IntrospectionEndpoint, IntrospectionHandler<TApp, TToken>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryIntrospection>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp, TToken>, AdviceIntrospectionProtectedResource<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp, TToken>, AdviceIntrospectionTokenValidation<TApp, TToken>>());
    }

    #endregion
}
