using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceIntrospectionProtectedResource{TApp}" />.</summary>
public static class AdviceIntrospectionProtectedResource
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Verifies the introspection requesting client is a confidential application with the
///     <c>endpoint:introspection</c> permission, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.1">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.1: Introspection Request
///     </seealso>
///     , and applies the deployment's caller-to-resource mapping from
///     <see cref="SchemataAuthorizationOptions.IntrospectionResourceAudiences" /> per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2">
///         RFC 7662 §2
///     </seealso>
///     : an access token is active only when at least one of its verified <c>aud</c> values names a
///     resource the caller represents, otherwise the response is the opaque <c>{ "active": false }</c>.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Public clients are rejected because they cannot be trusted to inspect tokens (RFC 6749 §2.1).
///     The caller-to-resource mapping deliberately applies only to access tokens; other token types
///     continue under their token-validity policy.
/// </remarks>
public sealed class AdviceIntrospectionProtectedResource<TApp>(
    IApplicationManager<TApp>              manager,
    IOptions<SchemataAuthorizationOptions> options
) : IIntrospectionAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IIntrospectionAdvisor<TApp> Members

    public int Order => AdviceIntrospectionProtectedResource.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp> introspection,
        CancellationToken                  ct = default
    ) {
        if (introspection.Application is { IsConfidential: false }) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.CLIENT_SECRET_REQUIRED, code: 401);
        }

        if (!await manager.HasPermissionAsync(introspection.Application, PermissionPrefixes.Endpoint + Endpoints.Introspect, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.UNAUTHORIZED_GRANT_TYPE, code: 403);
        }

        // RFC 7662 §2: activity is scoped to the protected resource making the call. This
        // deployment mapping deliberately applies only to access tokens.
        if (introspection.Token?.Type != TokenTypes.AccessToken) {
            return AdviseResult.Continue;
        }

        var caller = introspection.Application?.ClientId;
        if (string.IsNullOrWhiteSpace(caller)
         || !options.Value.IntrospectionResourceAudiences.TryGetValue(caller, out var audiences)
         || audiences.Count == 0) {
            return AdviseResult.Block;
        }

        if (introspection.Principal?.FindAll(Claims.Audience).Any(a => audiences.Contains(a.Value)) != true) {
            return AdviseResult.Block;
        }

        return AdviseResult.Continue;
    }

    #endregion
}
