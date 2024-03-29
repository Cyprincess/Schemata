using System.Threading.Tasks;
using OpenIddict.Server;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Introspection;
using Descriptor = OpenIddict.Server.OpenIddictServerHandlerDescriptor;

namespace Schemata.Authorization.Foundation.Handlers;

public class SchemataIntrospectionHandler : IOpenIddictServerHandler<ValidateIntrospectionRequestContext>
{
    public static Descriptor Descriptor { get; } = Descriptor.CreateBuilder<ValidateIntrospectionRequestContext>()
                                                             .UseScopedHandler<SchemataIntrospectionHandler>()
                                                             .SetOrder(ValidateAuthentication.Descriptor.Order + 1_000)
                                                             .SetType(OpenIddictServerHandlerType.Custom)
                                                             .Build();

    #region IOpenIddictServerHandler<ValidateIntrospectionRequestContext> Members

    public async ValueTask HandleAsync(ValidateIntrospectionRequestContext context) {
        // TODO: ValidateEndpointPermissions
        // TODO: AttachApplicationClaims

        await Task.CompletedTask;
    }

    #endregion
}
