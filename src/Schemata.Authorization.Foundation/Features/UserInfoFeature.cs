using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

public sealed class UserInfoFeature : IAuthorizationFlowFeature
{
    #region IAuthorizationFlowFeature Members

    public int Order => 3_000;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<UserInfoEndpoint, UserInfoHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserInfoAdvisor, AdviceUserInfoOpenIdRequirement>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryUserInfo>());
    }

    #endregion
}
