using System.Linq;
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

public static class AdviceAuthorizeResponseMode
{
    public const int DefaultOrder = AdviceAuthorizeEndpointPermission.DefaultOrder + 10_000_000;
}

public sealed class AdviceAuthorizeResponseMode<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeResponseMode.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (!options.Value.RequireResponseModeSafety) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(authz.ResponseMode)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.ResponseType)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var responseTypes = authz.Request.ResponseType.Split(' ');

        // OAuth 2.0 Multiple Response Types §5: query mode must not be used with token or id_token.
        if ((responseTypes.Contains(ResponseTypes.Token) || responseTypes.Contains(ResponseTypes.IdToken))
         && authz.ResponseMode == ResponseModes.Query) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.ResponseMode)) {
                RedirectUri  = authz.Request?.RedirectUri,
                State        = authz.Request?.State,
                ResponseMode = ResponseModes.Fragment,
            };
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
