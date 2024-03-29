using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Session;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataSessionHandler : IOpenIddictServerHandler<ValidateLogoutRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateLogoutRequestContext>()
                                                             .UseScopedHandler<SchemataSessionHandler>()
                                                             .SetOrder(ValidateAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateLogoutRequestContext> Members

    public async ValueTask HandleAsync(ValidateLogoutRequestContext context) {
        // TODO: ValidateClientPostLogoutRedirectUri
        // TODO: ValidateEndpointPermissions
        // TODO: ValidateAuthorizedParty

        await Task.CompletedTask;
    }

    #endregion
}
