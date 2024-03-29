using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Revocation;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataRevocationHandler : IOpenIddictServerHandler<ValidateRevocationRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateRevocationRequestContext>()
                                                             .UseScopedHandler<SchemataRevocationHandler>()
                                                             .SetOrder(ValidateAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateRevocationRequestContext> Members

    public async ValueTask HandleAsync(ValidateRevocationRequestContext context) {
        // TODO: ValidateEndpointPermissions
        // TODO: RevokeToken

        await Task.CompletedTask;
    }

    #endregion
}
