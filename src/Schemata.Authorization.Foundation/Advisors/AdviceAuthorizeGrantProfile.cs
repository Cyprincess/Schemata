using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeGrantProfile{TApp}" />.</summary>
public static class AdviceAuthorizeGrantProfile
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeGrantPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates authorized scopes and selects the trusted OAuth or OpenID Connect profile for
///     the complete authorization continuation.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <seealso cref="AdviceRequestScopeValidation{TApp}" />
public sealed class AdviceAuthorizeGrantProfile<TApp>(
    IApplicationManager<TApp> apps
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeGrantProfile.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var request = authz.Request;
        if (request is null) {
            return AdviseResult.Continue;
        }

        var scopes = ScopeParser.Parse(request.Scope);
        foreach (var scope in scopes) {
            await Permissions.RequireTrueAsync(await apps.HasScopeAsync(authz.Application, scope, ct),
                OAuthErrors.InvalidScope, SchemataResources.INVALID_SCOPE,
                configure: exception => Configure(exception, authz));
        }

        var oidc = scopes.Contains(Scopes.OpenId);
        var responses = ScopeParser.Parse(request.ResponseType);
        if (responses.Contains(ResponseTypes.IdToken) && !oidc) {
            throw Configure(new(OAuthErrors.InvalidScope, SchemataResources.INVALID_SCOPE), authz);
        }

        if (!oidc && (!string.IsNullOrWhiteSpace(request.Nonce)
                      || !string.IsNullOrWhiteSpace(request.IdTokenHint)
                      || scopes.Contains(Scopes.DeviceSso))) {
            throw Configure(new(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED), authz);
        }

        request.GrantProfile = oidc ? GrantProfiles.OpenIdConnect : GrantProfiles.OAuth;
        return AdviseResult.Continue;
    }

    #endregion

    private static OAuthException Configure(OAuthException exception, AuthorizeContext<TApp> authz) {
        exception.RedirectUri  = authz.Request?.RedirectUri;
        exception.State        = authz.Request?.State;
        exception.ResponseMode = authz.ResponseMode;
        return exception;
    }
}
