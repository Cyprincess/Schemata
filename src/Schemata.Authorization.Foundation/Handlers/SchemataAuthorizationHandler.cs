using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Authentication;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataAuthorizationHandler : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
                                                             .UseScopedHandler<SchemataAuthorizationHandler>()
                                                             .SetOrder(ValidateAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateAuthorizationRequestContext> Members

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context) {
        // TODO: ValidateResponseType
        // TODO: ValidateClientRedirectUri
        // TODO: ValidateScopes
        // TODO: ValidateEndpointPermissions
        // TODO: ValidateGrantTypePermissions
        // TODO: ValidateResponseTypePermissions
        // TODO: ValidateScopePermissions
        // TODO: ValidateProofKeyForCodeExchangeRequirement

        await Task.CompletedTask;
    }

    #endregion
}
