using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Device;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataDeviceHandler : IOpenIddictServerHandler<ValidateDeviceRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateDeviceRequestContext>()
                                                             .UseScopedHandler<SchemataDeviceHandler>()
                                                              // TODO: handlers for ValidateTokenContext, GenerateTokenContext
                                                             .SetOrder(ValidateDeviceAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateDeviceRequestContext> Members

    public async ValueTask HandleAsync(ValidateDeviceRequestContext context) {
        // TODO: ValidateScopes
        // TODO: ValidateEndpointPermissions
        // TODO: ValidateGrantTypePermissions
        // TODO: ValidateScopePermissions

        await Task.CompletedTask;
    }

    #endregion
}
