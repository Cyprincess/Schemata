using System.Collections.Generic;
using System;
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
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeClientAndRedirect{TApp}" />.</summary>
public static class AdviceAuthorizeClientAndRedirect
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Validates the client_id, resolves the application, validates the redirect_uri, and validates the response_type
///     and response_mode,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.2.1: Error Response
///     </seealso>
///     ,
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
///         OpenID Connect Core 1.0
///         §3.1.2.1: Authentication Request
///     </seealso>
///     ,
///     and
///     <seealso href="https://openid.net/specs/oauth-v2-multiple-response-types-1_0.html">
///         OAuth 2.0 Multiple Response Type
///         Encoding Practices 1.0
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Response types are sorted alphabetically so that <c>"id_token token"</c> and <c>"token id_token"</c>
///     are treated identically.
/// </remarks>
/// <seealso cref="AdviceAuthorizeEndpointPermission{TApp}" />
public sealed class AdviceAuthorizeClientAndRedirect<TApp>(
    IApplicationManager<TApp>              apps,
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
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.ClientId });
        }

        if (!string.IsNullOrWhiteSpace(authz.Request.ResponseMode)
         && !options.Value.AllowedResponseModes.Contains(authz.Request.ResponseMode)) {
            // OIDC Core §3.1.2.6: on the interactive authorization endpoint an unsupported
            // response mode has no legal delivery encoding, so the wire answer is the bare 400
            // status. Under PAR validation the RFC 9126 §2.3 JSON error response carries the
            // error instead. Validated BEFORE the callback is captured so the central
            // finalizer cannot inherit one.
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.ResponseMode }) {
                OmitErrorParameters = true,
            };
        }

        var application = await apps.FindByClientIdAsync(authz.Request.ClientId, ct);
        if (application is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        authz.Application = application;

        if (!await apps.ValidateRedirectUriAsync(authz.Application, authz.Request.RedirectUri, ct)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REDIRECT_URI);
        }

        // The redirect is now trusted: every later failure — success and error alike — finalizes
        // from this single captured callback (legal effective mode included), never from raw input.
        authz.Callback = new(authz.Request.RedirectUri!, authz.Request.State, authz.ResponseMode!);

        var type = authz.Request.ResponseType?.Split(' ').OrderBy(x => x).ToList() ?? [];
        authz.Request.ResponseType = string.Join(' ', type);

        if (!options.Value.AllowedResponseTypes.Contains(authz.Request.ResponseType)) {
            throw new OAuthException(OAuthErrors.UnsupportedResponseType, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.ResponseType }) {
                RedirectUri  = authz.Callback.RedirectUri,
                State        = authz.Callback.State,
                ResponseMode = authz.Callback.ResponseMode,
            };
        }

        // Per-client response-type check: the full normalized combination must appear in the
        // client's registered response_types metadata. Token order is insignificant on both
        // sides ("code id_token" and "id_token code" are the same entry).
        if (!await apps.HasResponseTypeAsync(authz.Application, authz.Request.ResponseType, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.ResponseType }) {
                RedirectUri  = authz.Callback.RedirectUri,
                State        = authz.Callback.State,
                ResponseMode = authz.Callback.ResponseMode,
            };
        }

        return AdviseResult.Continue;
    }

    #endregion

}
