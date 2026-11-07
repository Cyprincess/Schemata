using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the exclusive <c>none</c> response type on an authorization endpoint registered by
///     <see cref="AuthorizationCodeFlowFeature" />.
/// </summary>
public sealed class NoneFlowFeature : IAuthorizationFlowFeature
{
    public int Order => AuthorizationCodeFlowFeature.DefaultOrder + 30;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.Configure<SchemataAuthorizationOptions>(options => {
            options.AllowedResponseTypes.Add(ResponseTypes.None);
        });
    }
}
