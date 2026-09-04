using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Registers the Token Introspection endpoint per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>:
///     handler, resource protection and token validation advisors, and discovery metadata.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseIntrospection()</c> on <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />
///     .
/// </remarks>
public sealed class IntrospectionFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => IntrospectionFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddScoped<IntrospectionEndpoint, IntrospectionHandler<TApp>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryIntrospection>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp>, AdviceIntrospectionProtectedResource<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp>, AdviceIntrospectionTokenValidation<TApp>>());
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="IntrospectionFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class IntrospectionFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = UserInfoFeature.DefaultOrder + 100;
}
