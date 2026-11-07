using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants and the shared approval rule for <see cref="AdviceAuthorizeClaims{TApp}" />.</summary>
public static class AdviceAuthorizeClaims
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizePrompt.DefaultOrder + 10_000_000;

    /// <summary>
    ///     Validates the claims constraints against the final authentication event: a malformed
    ///     parameter rejects the approval with <c>invalid_request</c>, a <c>sub</c> requested
    ///     with a <c>value</c> the approving subject does not match rejects it with
    ///     <c>login_required</c> (§5.5.1: the authentication MUST fail), and an Essential
    ///     <c>acr</c> request the resolved event does not satisfy rejects it the same way.
    /// </summary>
    public static void ValidateApproval(string? value, string? acr, string? subject) {
        ClaimsRequest? request;
        try {
            request = ClaimsRequest.Parse(value);
        } catch (InvalidOperationException) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.CLAIMS_PARAMETER_NOT_VALID);
        }

        var pinned = request is null ? null : PinnedSubject(request);
        if (pinned is not null && !string.Equals(subject, pinned, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }

        var required = request?.EssentialAcrValues();
        if (required is { Count: > 0 } && (acr is null || !required.Contains(acr, StringComparer.Ordinal))) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }
    }

    /// <summary>The subject pinned by a <c>sub</c> claim requested with a <c>value</c> member.</summary>
    public static string? PinnedSubject(ClaimsRequest request) {
        var spec = request.IdToken?.GetValueOrDefault(IdentityClaims.Subject)
                ?? request.Userinfo?.GetValueOrDefault(IdentityClaims.Subject);
        return spec?.Value;
    }

    internal static Task<ResourceAuthenticationRequirement?> Evaluate(
        IResourceAuthenticationRequirementProvider requirements,
        ClaimsPrincipal? principal,
        string? clientId,
        string? resource,
        IReadOnlyList<string> requestedAcr,
        long? maxAge,
        CancellationToken ct
    ) {
        var policyPrincipal = principal is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));
        if (!string.IsNullOrWhiteSpace(clientId)
         && policyPrincipal.Identity is ClaimsIdentity identity
         && policyPrincipal.FindFirst(Claims.ClientId) is null) {
            identity.AddClaim(new(Claims.ClientId, clientId));
        }

        return requirements.EvaluateAsync(
            new(policyPrincipal, resource, requestedAcr, maxAge), ct);
    }
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
    IAuthenticationContextProvider? contexts = null,
    IOptions<SchemataAuthorizationOptions>? options = null,
    IResourceAuthenticationRequirementProvider? requirements = null,
    TimeProvider? time = null
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeClaims.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (authz.Request is null) {
            return AdviseResult.Continue;
        }

        if (authz.Stage != AuthorizationRequestStage.Pushed) {
            await ApplyResourceRequirement(authz, ct);
        }

        ClaimsRequest? request;
        try {
            request = ClaimsRequest.Parse(authz.Request.Claims);
        } catch (InvalidOperationException) {
            throw Redirect(authz, OAuthErrors.InvalidRequest, SchemataResources.CLAIMS_PARAMETER_NOT_VALID);
        }
        if (request is null) return AdviseResult.Continue;
        authz.Request.RequestedClaims = request;
        if (authz.Request.GrantProfile == GrantProfiles.OAuth && request.IdToken is not null) {
            throw Redirect(authz, OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED);
        }


        if (authz.Stage == AuthorizationRequestStage.Pushed) {
            return AdviseResult.Continue;
        }

        var subject = authz.Principal?.FindFirst(IdentityClaims.Subject)?.Value;
        var pinned  = AdviceAuthorizeClaims.PinnedSubject(request);
        if (pinned is not null && !string.Equals(subject, pinned, StringComparison.Ordinal)) {
            // §5.5.1: a sub value mismatch MUST cause the authentication to fail. Silent
            // requests cannot recover; interactive ones re-authenticate as the pinned subject.
            if (IsSilent(authz)) {
                throw Redirect(authz, OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
            }

            authz.RequireReauthentication = true;
        }

        var essentialAcr = request.EssentialAcrValues();
        if (essentialAcr is { Count: > 0 }) {
            var sid = authz.Request.OpSessionId;
            var context = await AuthenticationContextExtensions.ResolveAsync(
                authz.Principal,
                authz.Request.Authentication,
                sid,
                options?.Value.SessionIdClaimType ?? Claims.SessionId,
                contexts,
                ct);
            authz.Request.Authentication = context;
            var current = context?.Acr;
            var satisfied = current is not null && essentialAcr.Contains(current, StringComparer.Ordinal);

            if (!satisfied && authz.Principal?.Identity?.IsAuthenticated == true) {
                if (IsSilent(authz)) {
                throw Redirect(authz, OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
                }

                authz.RequireReauthentication = true;
            }

            // Forward the essential values to the login pipeline through acr_values; the
            // voluntary acr_values semantics (§5.5.1.1) apply once the user re-authenticates.
            var existing = authz.Request.AcrValueTokens.ToList();
            var added    = essentialAcr.Where(v => !existing.Contains(v, StringComparer.Ordinal)).ToList();
            if (added.Count > 0) {
                existing.AddRange(added);
                authz.Request.AcrValues      = string.Join(' ', existing);
                authz.Request.AcrValueTokens = new(existing, StringComparer.Ordinal);
            }
        }

        return AdviseResult.Continue;
    }

    #endregion

    private async Task ApplyResourceRequirement(AuthorizeContext<TApp> authz, CancellationToken ct) {
        if (requirements is null || authz.Request is not { } request) {
            return;
        }

        long? requestedAge = null;
        if (!string.IsNullOrWhiteSpace(request.MaxAge)) {
            if (!long.TryParse(request.MaxAge, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) {
                throw Redirect(authz, OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED);
            }
            requestedAge = parsed;
        }

        var requestedAcr = request.AcrValues?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var resource = request.Resource?.FirstOrDefault()
                    ?? options?.Value.DefaultResource
                    ?? options?.Value.Issuer;
        var requirement = await AdviceAuthorizeClaims.Evaluate(
            requirements, authz.Principal, request.ClientId, resource, requestedAcr, requestedAge, ct);
        if (requirement is null) {
            return;
        }

        var authentication = await AuthenticationContextExtensions.ResolveAsync(
            authz.Principal,
            request.Authentication,
            request.OpSessionId,
            options?.Value.SessionIdClaimType ?? Claims.SessionId,
            contexts,
            ct);
        request.Authentication = authentication;
        if (requirement.IsSatisfiedBy(authentication?.Acr, authentication?.AuthTime, _time.GetUtcNow())) {
            return;
        }

        if (IsSilent(authz)) {
            throw Redirect(authz, OAuthErrors.UnmetAuthenticationRequirements, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }

        var acrValues = requestedAcr.ToList();
        acrValues.AddRange(requirement.AcrValues.Where(value => !acrValues.Contains(value, StringComparer.Ordinal)));
        if (acrValues.Count > 0) {
            request.AcrValues = string.Join(' ', acrValues);
            request.AcrValueTokens = new(acrValues, StringComparer.Ordinal);
        }

        if (requirement.MaxAge is { } maxAge) {
            var seconds = (long)maxAge.TotalSeconds;
            if (seconds < 0 || maxAge.TotalSeconds != seconds) {
                throw new InvalidOperationException("Resource max_age must be a non-negative whole number of seconds.");
            }
            if (requestedAge is not { } currentAge || seconds < currentAge) {
                request.MaxAge = seconds.ToString(CultureInfo.InvariantCulture);
            }
        }

        authz.RequireReauthentication = true;
    }

    private static OAuthException Redirect(AuthorizeContext<TApp> authz, string error, string resourceKey) {
        return new(error, resourceKey) {
            RedirectUri  = authz.Request?.RedirectUri,
            State        = authz.Request?.State,
            ResponseMode = authz.ResponseMode,
        };
    }

    private static bool IsSilent(AuthorizeContext<TApp> authz) {
        return authz.Request?.Prompts.Contains(PromptValues.None) == true;
    }


}
