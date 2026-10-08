using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the dynamic client registration replace capability, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc7592.html#section-3">
///     RFC 7592: OAuth 2.0 Dynamic Client Registration Management Protocol §3: Client Update Request
/// </seealso>
/// .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRegistrationReplace()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />, on top of
///     <c>UseDynamicClientRegistration()</c>. Without it the PUT management route is absent
///     and local dispatch of <see cref="RegisterReplaceQuery" /> has no handler.
/// </remarks>
public sealed class RegistrationReplaceFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication, new()
{
    #region IAuthorizationFlowFeature Members

    public int Order => RegistrationReplaceFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<RegisterEndpoint, RegisterHandler<TApp>>();
        services.TryAddScoped<
            IRequestHandler<RegisterReplaceQuery, RegistrationResponse?>,
            EndpointDispatchHandler<RegisterReplaceQuery, RegisterEndpoint, RegistrationResponse?>>();
    }

    #endregion
}

/// <summary>
///     Ordering anchor for <see cref="RegistrationReplaceFeature{TApp}" /> so successor features can
///     chain off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class RegistrationReplaceFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = DynamicRegistrationFeature.DefaultOrder + 100;
}
