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
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeClientAndRedirect{TApp}" />.</summary>
public static class AdviceAuthorizeClientAndRedirect
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceAuthorizeClientAndRedirect<TApp>(
    IApplicationManager<TApp>              manager,
    IOptions<SchemataAuthorizationOptions> options
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeClientAndRedirect.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (string.IsNullOrWhiteSpace(authz.Request?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidClient, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.ClientId));
        }

        var application = await manager.FindByCanonicalNameAsync(authz.Request.ClientId, ct);
        if (application is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        authz.Application = application;

        if (!await manager.ValidateRedirectUriAsync(authz.Application, authz.Request.RedirectUri, ct)) {
            throw new OAuthException(OAuthErrors.InvalidRedirectUri, SchemataResources.GetResourceString(SchemataResources.ST4009));
        }

        var type = authz.Request.ResponseType?.Split(' ').OrderBy(x => x).ToList() ?? [];
        authz.Request.ResponseType = string.Join(' ', type);

        if (!options.Value.AllowedResponseTypes.Contains(authz.Request.ResponseType)) {
            throw new OAuthException(OAuthErrors.UnsupportedResponseType, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.ResponseType)) {
                RedirectUri  = authz.Request.RedirectUri,
                State        = authz.Request.State,
                ResponseMode = authz.Request.ResponseMode,
            };
        }

        if (!string.IsNullOrWhiteSpace(authz.Request.ResponseMode)
         && !options.Value.AllowedResponseModes.Contains(authz.Request.ResponseMode)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.ResponseMode)) {
                RedirectUri  = authz.Request.RedirectUri,
                State        = authz.Request.State,
                ResponseMode = ResponseModes.Query,
            };
        }

        return AdviseResult.Continue;
    }

    #endregion
}
