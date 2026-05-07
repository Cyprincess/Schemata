using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceIntrospectionProtectedResource{TApp, TToken}" />.</summary>
public static class AdviceIntrospectionProtectedResource
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Verifies the introspection requesting client is a confidential application with the
///     <c>endpoint:introspection</c> permission, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.1">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.1: Introspection Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <remarks>
///     Public clients are rejected because they cannot be trusted to inspect tokens (RFC 6749: The OAuth 2.0
///     Authorization Framework §2.1).
/// </remarks>
public sealed class AdviceIntrospectionProtectedResource<TApp, TToken>(IApplicationManager<TApp> manager) : IIntrospectionAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IIntrospectionAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceIntrospectionProtectedResource.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp, TToken> introspection,
        CancellationToken                  ct = default
    ) {
        if (introspection.Application?.ClientType == ClientTypes.Public) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4002),
                401
            );
        }

        if (!await manager.HasPermissionAsync(introspection.Application, PermissionPrefixes.Endpoint + Endpoints.Introspect, ct)) {
            throw new OAuthException(
                OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.ST4007),
                403
            );
        }

        return AdviseResult.Continue;
    }

    #endregion
}
