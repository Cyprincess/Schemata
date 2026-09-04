using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the OAuth 2.0 Authorization Code flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1: Authorization Code Grant
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html">
///         RFC 7636: Proof Key for Code Exchange by OAuth Public
///         Clients
///     </seealso>
///     :
///     authorize and token exchange endpoints, PKCE advisors, consent, and the discovery metadata for the
///     <c>code</c> grant.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <typeparam name="TScope">The scope entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseCodeFlow()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
/// <seealso cref="IAuthorizationFlowFeature" />
public sealed class AuthorizationCodeFlowFeature<TApp, TAuth, TScope> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
{
    #region IAuthorizationFlowFeature Members

    public int Order => AuthorizationCodeFlowFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.Configure<SchemataAuthorizationOptions>(o => {
            o.AllowedResponseTypes.Add(ResponseTypes.Code);
            o.AllowedResponseModes.Add(ResponseModes.FormPost);
        });

        services.PostConfigure<SchemataAuthorizationOptions>(o => {
            if (string.IsNullOrWhiteSpace(o.InteractionUri)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.REQUIRED_SETTING_MISSING), "Code flow", nameof(o.InteractionUri)));
            }

            if (!Uri.TryCreate(o.InteractionUri, UriKind.Absolute, out var _)) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ABSOLUTE_URI_REQUIRED), nameof(o.InteractionUri)));
            }
        });

        services.TryAddScoped<AuthorizeEndpoint, AuthorizeHandler<TApp>>();

        services.TryAddKeyedScoped<IGrantHandler, AuthorizationCodeHandler<TApp>>(GrantTypes.AuthorizationCode);
        services.TryAddKeyedScoped<IInteractionHandler, AuthorizeInteractionHandler<TApp, TAuth, TScope>>(TokenTypeUris.Interaction);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryCodeFlow>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeClientAndRedirect<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeEndpointPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeGrantPermission<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeScopeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizePkce<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeNonce<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeResponseMode<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizePrompt<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeConsent<TApp, TAuth>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeAutoApproveSignIn<TApp, TAuth>>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp>, AdviceCodeExchangeValidation<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICodeExchangeAdvisor<TApp>, AdviceCodeExchangePkce<TApp>>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="AuthorizationCodeFlowFeature{TApp, TAuth, TScope}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class AuthorizationCodeFlowFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = RevocationFeature.DefaultOrder + 100;
}
