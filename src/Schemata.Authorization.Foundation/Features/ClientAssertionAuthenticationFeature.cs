using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Offers JWT assertion client authentication as an opt-in flow feature, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-2">
///         RFC 7523: JSON Web Token (JWT) Profile for OAuth 2.0 Client
///         Authentication and Authorization Grants §2: Client Authentication
///     </seealso>
///     : the <c>client_secret_jwt</c> and <c>private_key_jwt</c> channels join the client
///     authentication chain. DI presence is the switch: without the feature only the
///     RFC 6749 baseline channels (<c>client_secret_basic</c>, <c>client_secret_post</c>)
///     are registered, and the discovery metadata mirrors the server's
///     <c>AllowedClientAuthMethods</c> set — add the assertion method names there when the
///     feature is enabled.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseClientAssertionAuthentication()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />. Every token-style
///     endpoint (token, introspection, revocation, and later PAR/backchannel) picks the
///     channels up through the shared <c>IClientAuthenticationService</c> automatically.
/// </remarks>
public sealed class ClientAssertionAuthenticationFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => ClientAssertionAuthenticationFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddSingleton<ClientAssertionValidator>();
        services.TryAddSingleton<ClientAssertionChannel>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, ClientSecretJwtAuthentication<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClientAuthentication<TApp>, PrivateKeyJwtAuthentication<TApp>>());
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="ClientAssertionAuthenticationFeature{TApp}" /> so successor
///     features can chain off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class ClientAssertionAuthenticationFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = ClaimsParameterFeature.DefaultOrder + 100;
}
