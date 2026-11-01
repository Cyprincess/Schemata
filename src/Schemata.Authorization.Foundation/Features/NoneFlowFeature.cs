using System;
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
        if (!schemata.HasFeature(typeof(AuthorizationCodeFlowFeature<,,,,>))) {
            throw new InvalidOperationException(
                "The none flow requires an authorize endpoint; call UseAuthorizationCodeFlow() before UseNoneFlow().");
        }

        services.Configure<SchemataAuthorizationOptions>(options => {
            options.AllowedResponseTypes.Add(ResponseTypes.None);
        });
    }
}
