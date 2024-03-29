using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Exchange;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataExchangeHandler : IOpenIddictServerHandler<ValidateTokenRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateTokenRequestContext>()
                                                             .UseScopedHandler<SchemataExchangeHandler>()
                                                             .SetOrder(ValidateAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateTokenRequestContext> Members

    public async ValueTask HandleAsync(ValidateTokenRequestContext context) {
        // TODO: ValidateScopes
        // TODO: ValidateEndpointPermissions
        // TODO: ValidateGrantTypePermissions
        // TODO: ValidateScopePermissions
        // TODO: ValidateProofKeyForCodeExchangeRequirement

        await Task.CompletedTask;
    }

    #endregion
}
