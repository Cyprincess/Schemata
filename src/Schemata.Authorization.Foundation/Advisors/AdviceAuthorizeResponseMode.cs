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

/// <summary>Order constants for <see cref="AdviceAuthorizeResponseMode{TApp}" />.</summary>
public static class AdviceAuthorizeResponseMode
{
    public const int DefaultOrder = AdviceAuthorizeEndpointPermission.DefaultOrder + 10_000_000;
}

/// <summary>Enforces response_mode safety per OAuth 2.0 Multiple Response Types §5.</summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     OAuth 2.0 Multiple Response Types §5 requires that the query response mode not be used with
///     <c>token</c> or <c>id_token</c> response types because fragments are needed to keep tokens
///     out of referrer headers and server logs. This check is only enforced when
///     <see cref="CodeFlowOptions.RequireResponseModeSafety" /> is true.
/// </remarks>
/// <seealso cref="CodeFlowOptions" />
public sealed class AdviceAuthorizeResponseMode<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeResponseMode.DefaultOrder;

    /// <inheritdoc />
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
