using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Features;

public sealed class AuthorizationCodeFlowFeature<TApp, TAuth, TScope, TToken> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => 10_100;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        var options = configurators.PopOrDefault<CodeFlowOptions>();
        services.Configure(options);

        services.Configure<SchemataAuthorizationOptions>(o => {
            o.AllowedResponseTypes.Add(ResponseTypes.Code);
            o.AllowedResponseModes.Add(ResponseModes.FormPost);
        });

        services.TryAddScoped<AuthorizeEndpoint, AuthorizeHandler<TApp, TToken>>();

        services.TryAddKeyedScoped<IGrantHandler, AuthorizationCodeHandler<TApp, TToken>>(GrantTypes.AuthorizationCode);
        services.TryAddKeyedScoped<IInteractionHandler, AuthorizeInteractionHandler<TApp, TAuth, TScope, TToken>>(TokenTypeUris.Interaction);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryCodeFlow>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeClientAndRedirect<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeScopeValidation<TApp, TScope>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizePkce<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeNonce<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeResponseMode<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizePrompt<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeConsent<TApp, TAuth>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeAutoApproveSignIn<TApp, TAuth>>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp, TToken>, AdviceCodeExchangeValidation<TApp, TToken>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp, TToken>, AdviceCodeExchangePkce<TApp, TToken>>());
    }

    #endregion
}
