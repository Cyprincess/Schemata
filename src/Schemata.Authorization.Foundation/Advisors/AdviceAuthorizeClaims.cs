using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeClaims{TApp}" />.</summary>
public static class AdviceAuthorizeClaims
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizePrompt.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates the <c>claims</c> authorization request parameter, per
/// <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#ClaimsParameter">
///         OpenID Connect Core 1.0 §5.5: Requesting Claims using the "claims" Request
///         Parameter
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     <para>
///         A malformed parameter value rejects the request with <c>invalid_request</c>. The
///         <c>sub</c> claim requested with a <c>value</c> member pins the request to that
///         subject: when the current session identifies a different subject the
///         authentication MUST fail (§5.5.1), which this server expresses as
///         <c>login_required</c> under <c>prompt=none</c> and a re-authentication
///         requirement otherwise.
///     </para>
///     <para>
///         An Essential <c>acr</c> claim request with <c>values</c> (§5.5.1.1) carries
///         MUST-reject semantics: when the current authentication context cannot satisfy
///         any requested value the outcome is a failed authentication attempt —
///         <c>login_required</c> under <c>prompt=none</c>, re-authentication otherwise, with
///         the requested values appended to <c>acr_values</c> so the login pipeline can
///         satisfy them. §5.5.1.1 leaves the combined use of <c>acr_values</c> and an
///         individual essential request unspecified; this server unions the two value sets.
///     </para>
/// </remarks>
public sealed class AdviceAuthorizeClaims<TApp>(
    IAuthenticationContextProvider? contexts = null
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeClaims.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        ClaimsRequest? request;
        try {
            request = ClaimsRequest.Parse(authz.Request?.Claims);
        } catch (InvalidOperationException ex) {
            throw Redirect(authz, OAuthErrors.InvalidRequest, ex.Message);
        }

        if (request is null) {
            return AdviseResult.Continue;
        }

        if (request.Userinfo is not null && authz.Request?.Scope is not null
         && !authz.Request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Contains(Scopes.OpenId, StringComparer.Ordinal)) {
            // §5.5: the userinfo member requires a response_type that issues an access token,
            // which at this server means an openid-scoped request.
            throw Redirect(authz, OAuthErrors.InvalidRequest, "The claims userinfo member requires the openid scope.");
        }

        var subject = authz.Principal?.FindFirst(IdentityClaims.Subject)?.Value;
        var pinned  = PinnedSubject(request);
        if (pinned is not null && !string.Equals(subject, pinned, StringComparison.Ordinal)) {
            // §5.5.1: a sub value mismatch MUST cause the authentication to fail. Silent
            // requests cannot recover; interactive ones re-authenticate as the pinned subject.
            if (IsSilent(authz)) {
                throw Redirect(authz, OAuthErrors.LoginRequired,
                    SchemataResources.GetResourceString(SchemataResources.USER_AUTHENTICATION_REQUIRED));
            }

            authz.RequireReauthentication = true;
        }

        var essentialAcr = EssentialAcrValues(request);
        if (essentialAcr is { Count: > 0 }) {
            var context = contexts is null ? null : await contexts.GetContextAsync(authz.Principal, ct);
            var current = context?.Acr;
            var satisfied = current is not null && essentialAcr.Contains(current, StringComparer.Ordinal);

            if (!satisfied && authz.Principal?.Identity?.IsAuthenticated == true) {
                if (IsSilent(authz)) {
                    throw Redirect(authz, OAuthErrors.LoginRequired,
                        SchemataResources.GetResourceString(SchemataResources.USER_AUTHENTICATION_REQUIRED));
                }

                authz.RequireReauthentication = true;
            }

            // Forward the essential values to the login pipeline through acr_values; the
            // voluntary acr_values semantics (§5.5.1.1) apply once the user re-authenticates.
            var existing = authz.Request?.AcrValues?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList() ?? [];
            existing.AddRange(essentialAcr.Where(v => !existing.Contains(v, StringComparer.Ordinal)));
            if (authz.Request is not null && existing.Count > 0) {
                authz.Request.AcrValues = string.Join(' ', existing);
            }
        }

        return AdviseResult.Continue;
    }

    #endregion

    private static OAuthException Redirect(AuthorizeContext<TApp> authz, string error, string description) {
        return new OAuthException(error, description) {
            RedirectUri  = authz.Request?.RedirectUri,
            State        = authz.Request?.State,
            ResponseMode = authz.ResponseMode,
        };
    }

    private static bool IsSilent(AuthorizeContext<TApp> authz) {
        return authz.Request?.Prompt?
                   .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Contains(PromptValues.None, StringComparer.Ordinal) == true;
    }

    private static string? PinnedSubject(ClaimsRequest request) {
        var spec = request.IdToken?.GetValueOrDefault(IdentityClaims.Subject)
                ?? request.Userinfo?.GetValueOrDefault(IdentityClaims.Subject);
        return spec?.Value;
    }

    private static List<string>? EssentialAcrValues(ClaimsRequest request) {
        if (request.IdToken?.GetValueOrDefault(Claims.Acr) is not { Essential: true } acr) {
            return null;
        }

        if (acr.Values is { Count: > 0 } values) {
            return values;
        }

        return acr.Value is { Length: > 0 } single ? [single] : null;
    }
}
