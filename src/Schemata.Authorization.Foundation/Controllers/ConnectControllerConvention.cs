using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Controllers;

/// <summary>
///     Removes <see cref="ConnectController" /> actions whose backing closed request handler is
///     not installed. Route absence — not a runtime dispatch failure — is the contract for an
///     inactive capability: an unauthenticated request to a removed action receives the normal
///     unmatched-route 404 (405 when another method on the same path remains) and no dispatch is
///     attempted. An installed handler whose construction is genuinely broken still fails
///     observably at dispatch; this convention never masks that case.
/// </summary>
public sealed class ConnectControllerConvention : IControllerModelConvention
{
    /// <summary>
    ///     Binds each Connect action to the closed request handler that backs it; the action is
    ///     routable exactly when that handler is registered.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> Handlers = new Dictionary<string, Type> {
        [nameof(ConnectController.AuthorizeGet)]       = typeof(IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.AuthorizePost)]      = typeof(IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.CheckSession)]       = typeof(IRequestHandler<CheckSessionEndpointRequest, string>),
        [nameof(ConnectController.Device)]             = typeof(IRequestHandler<DeviceAuthorizeEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.EndSessionGet)]      = typeof(IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.EndSessionPost)]     = typeof(IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.Interact)]           = typeof(IRequestHandler<InteractionDetailsQuery, AuthorizationResult>),
        [nameof(ConnectController.ApproveInteraction)] = typeof(IRequestHandler<InteractionApproveRequest, AuthorizationResult>),
        [nameof(ConnectController.DenyInteraction)]    = typeof(IRequestHandler<InteractionDenyRequest, Unit>),
        [nameof(ConnectController.Introspect)]         = typeof(IRequestHandler<IntrospectionEndpointQuery, IntrospectionResponse>),
        [nameof(ConnectController.Par)]                = typeof(IRequestHandler<ParEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.Profile)]            = typeof(IRequestHandler<UserInfoEndpointQuery, AuthorizationResult>),
        [nameof(ConnectController.Revoke)]             = typeof(IRequestHandler<RevokeEndpointRequest, Unit>),
        [nameof(ConnectController.Token)]              = typeof(IRequestHandler<TokenEndpointRequest, AuthorizationResult>),
        [nameof(ConnectController.Register)]           = typeof(IRequestHandler<RegisterEndpointQuery, RegistrationResponse>),
        [nameof(ConnectController.RegisterRead)]       = typeof(IRequestHandler<RegisterReadQuery, RegistrationResponse?>),
        [nameof(ConnectController.RegisterReplace)]    = typeof(IRequestHandler<RegisterReplaceQuery, RegistrationResponse?>),
        [nameof(ConnectController.RegisterDelete)]     = typeof(IRequestHandler<RegisterDeleteQuery, bool>),
    };

    private readonly IServiceProvider _services;

    public ConnectControllerConvention(IServiceProvider services) { _services = services; }

    #region IControllerModelConvention Members

    public void Apply(ControllerModel controller) {
        if (controller.ControllerType != typeof(ConnectController)) {
            return;
        }

        var activation = _services.GetRequiredService<IServiceProviderIsService>();

        var issuer = CanonicalIssuer.Validate(_services.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value.Issuer);
        foreach (var selector in controller.Selectors) {
            if (selector.AttributeRouteModel is { } route) {
                route.Template = "~" + CanonicalIssuer.Path(issuer) + "/Connect";
            }
        }


        for (var i = controller.Actions.Count - 1; i >= 0; i--) {
            if (!Handlers.TryGetValue(controller.Actions[i].ActionName, out var handler)
             || !activation.IsService(handler)) {
                controller.Actions.RemoveAt(i);
            }
        }
    }

    #endregion
}
