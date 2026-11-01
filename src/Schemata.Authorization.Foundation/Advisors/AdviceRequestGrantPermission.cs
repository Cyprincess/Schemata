using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
/// <c>AdviceRequestDeviceCodePolling</c>
public sealed class AdviceRequestGrantPermission<TApp>(IApplicationManager<TApp> manager) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceRequestGrantPermission.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        TokenRequestContext<TApp> request,
        CancellationToken         ct = default
    ) {
        var application = request.Application
            ?? throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        var grantType = request.Request?.GrantType
            ?? throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.GrantType });
        // The registered grant_types metadata is the runtime authority.
        await Permissions.RequireTrueAsync(await manager.HasGrantTypeAsync(application, grantType, ct));

        return AdviseResult.Continue;
    }

    #endregion
}
