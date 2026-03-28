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

public static class AdviceIntrospectionProtectedResource
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceIntrospectionProtectedResource<TApp, TToken>(IApplicationManager<TApp> manager) : IIntrospectionAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IIntrospectionAdvisor<TApp,TToken> Members

    public int Order => AdviceIntrospectionProtectedResource.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp, TToken> introspection,
        CancellationToken                  ct = default
    ) {
        if (introspection.Application?.ClientType == ClientTypes.Public) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4002), 401);
        }

        if (!await manager.HasPermissionAsync(introspection.Application, PermissionPrefixes.Endpoint + "introspection", ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.GetResourceString(SchemataResources.ST4007), 403);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
