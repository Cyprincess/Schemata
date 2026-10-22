using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables the hybrid response types <c>code id_token</c>, <c>code token</c>, and
///     <c>code id_token token</c> on an authorization endpoint registered by
///     <see cref="AuthorizationCodeFlowFeature" />.
/// </summary>
public sealed class HybridFlowFeature : IAuthorizationFlowFeature
{
    public int Order => AuthorizationCodeFlowFeature.DefaultOrder + 20;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        if (!schemata.HasFeature(typeof(AuthorizationCodeFlowFeature<,,,,>))) {
            throw new InvalidOperationException(
                "The hybrid flow requires an authorize endpoint; call UseAuthorizationCodeFlow() before UseHybridFlow().");
        }

        services.Configure<SchemataAuthorizationOptions>(options => {
            options.AllowedResponseTypes.Add(ResponseTypes.Code + " " + ResponseTypes.IdToken);
            options.AllowedResponseTypes.Add(ResponseTypes.Code + " " + ResponseTypes.Token);
            options.AllowedResponseTypes.Add(ResponseTypes.Code + " " + ResponseTypes.IdToken + " " + ResponseTypes.Token);
            options.AllowedResponseModes.Add(ResponseModes.Fragment);
        });
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryImplicitGrant>());
    }
}
