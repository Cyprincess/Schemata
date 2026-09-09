using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

internal sealed class AdviceAuthorizationRequestRepresentation<TApp> : IAuthorizeRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    public int Order => SchemataConstants.Orders.Max;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (!string.IsNullOrWhiteSpace(authz.Request?.RequestUri) && !ctx.Has<ParRequestResolved>()) {
            throw new OAuthException(
                OAuthErrors.RequestUriNotSupported,
                SchemataResources.GetResourceString(SchemataResources.REQUEST_URI_NOT_SUPPORTED));
        }

        if (!string.IsNullOrWhiteSpace(authz.Request?.Request) && !ctx.Has<RequestObjectResolved>()) {
            throw new OAuthException(
                OAuthErrors.RequestNotSupported,
                SchemataResources.GetResourceString(SchemataResources.REQUEST_NOT_SUPPORTED));
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}

internal sealed class RequestObjectResolved { }
