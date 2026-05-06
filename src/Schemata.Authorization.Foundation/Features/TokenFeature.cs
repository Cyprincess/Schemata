using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>Registers the token endpoint handler, shared by all grant types.</summary>
/// <remarks>
///     Installed automatically by flow features that require a token endpoint
///     (<see cref="AuthorizationCodeFlowFeature{TApp, TAuth, TScope, TToken}" /> etc.).
/// </remarks>
/// <seealso cref="IAuthorizationFlowFeature" />
public sealed class TokenFeature : IAuthorizationFlowFeature
{
    #region IAuthorizationFlowFeature Members

    /// <inheritdoc cref="IAuthorizationFlowFeature.Order" />
    public int Order => 1_000;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<TokenEndpoint, TokenHandler>();
    }

    #endregion
}
