using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the implicit response types <c>token</c>, <c>id_token</c>, and
///     <c>id_token token</c> on an authorization endpoint registered by
///     <see cref="AuthorizationCodeFlowFeature" />.
/// </summary>
public sealed class ImplicitFlowFeature : IAuthorizationFlowFeature
{
    public int Order => AuthorizationCodeFlowFeature.DefaultOrder + 10;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.Configure<SchemataAuthorizationOptions>(options => {
            options.AllowedResponseTypes.Add(ResponseTypes.Token);
            options.AllowedResponseTypes.Add(ResponseTypes.IdToken);
            options.AllowedResponseTypes.Add(ResponseTypes.IdToken + " " + ResponseTypes.Token);
            options.AllowedResponseModes.Add(ResponseModes.Fragment);
        });
    }
}
