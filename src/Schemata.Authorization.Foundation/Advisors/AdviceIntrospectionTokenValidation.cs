using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceIntrospectionTokenValidation
{
    public const int DefaultOrder = AdviceIntrospectionProtectedResource.DefaultOrder + 10_000_000;
}

public sealed class AdviceIntrospectionTokenValidation<TApp, TToken> : IIntrospectionAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IIntrospectionAdvisor<TApp,TToken> Members

    public int Order => AdviceIntrospectionTokenValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp, TToken> introspection,
        CancellationToken                  ct = default
    ) {
        var token = introspection.Token;

        if (token?.Type != TokenTypes.AccessToken && token?.Type != TokenTypes.RefreshToken) {
            return Task.FromResult(AdviseResult.Block);
        }

        if (token.Status != TokenStatuses.Valid) {
            return Task.FromResult(AdviseResult.Block);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
