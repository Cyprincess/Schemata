using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceAuthorizeNonce
{
    public const int DefaultOrder = AdviceAuthorizePkce.DefaultOrder + 10_000_000;
}

public sealed class AdviceAuthorizeNonce<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeNonce.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (options.Value.RequireNonce
         && !string.IsNullOrWhiteSpace(authz.Request?.ResponseType)
         && authz.Request.ResponseType.Contains(ResponseTypes.IdToken)
         && string.IsNullOrWhiteSpace(authz.Request.Nonce)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.Nonce));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
