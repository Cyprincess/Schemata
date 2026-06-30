using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRequestGrantPermission{TApp}" />.</summary>
public static class AdviceRequestGrantPermission
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceRequestEndpointPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Checks that the application has the requested grant type permission at the token endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceRequestEndpointPermission{TApp}" />
/// <seealso cref="AdviceRequestDeviceCodePolling{TApp}" />
public sealed class AdviceRequestGrantPermission<TApp>(IApplicationManager<TApp> manager) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceRequestGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        var grant = request.GrantType;

        await PermissionAdvice.RequireAsync(manager, application, PermissionPrefixes.GrantType + grant, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
