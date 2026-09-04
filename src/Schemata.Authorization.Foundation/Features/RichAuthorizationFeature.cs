using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Core;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Enables rich authorization requests per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html">RFC 9396: OAuth 2.0 Rich Authorization Requests</seealso>
///     : registers the <c>authorization_details</c> validating and introspection-echo advisors and
///     advertises the registered detail types. Without the feature the parameter binds but stays
///     inert — it is neither validated nor granted (RFC 6749 §3.1 unrecognized-parameter posture).
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UseRichAuthorizationRequests()</c>. Detail-type descriptors are host-registered
///     <see cref="IAuthorizationDetailTypeDescriptor" /> services; consent-page presentation of the granted
///     details is the host's responsibility (the interaction payload carries them).
/// </remarks>
public sealed class RichAuthorizationFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => RichAuthorizationFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddSingleton<AuthorizationDetailsService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizeAdvisor<TApp>, AdviceAuthorizeAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntrospectionAdvisor<TApp>, AdviceIntrospectionAuthorizationDetails<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRichAuthorization>());
    }

    #endregion
}

/// <summary>
///     Publishes <c>authorization_details_types_supported</c> from the registered type descriptors, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-10">
///     RFC 9396: OAuth 2.0 Rich Authorization
///     Requests §10: Metadata
/// </seealso>
///     .
/// </summary>
public sealed class AdviceDiscoveryRichAuthorization(IEnumerable<IAuthorizationDetailTypeDescriptor> descriptors) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryJwtBearerGrant.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, DiscoveryContext discovery, CancellationToken ct = default) {
        var types = descriptors.Select(d => d.Type).Distinct().ToList();
        if (types.Count > 0) {
            discovery.Document ??= new();
            discovery.Document.AuthorizationDetailsTypesSupported = types;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="RichAuthorizationFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class RichAuthorizationFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = JwtBearerGrantFeature.DefaultOrder + 100;
}
