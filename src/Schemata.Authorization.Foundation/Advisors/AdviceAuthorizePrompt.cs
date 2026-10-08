using System;
using System.Collections.Generic;
using System.Linq;
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
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants and known prompt values for <see cref="AdviceAuthorizePrompt{TApp}" />.</summary>
public static class AdviceAuthorizePrompt
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeDpopJkt.DefaultOrder + 10_000_000;

    /// <summary>The known prompt values defined by OpenID Connect Core 1.0 §3.1.2.1.</summary>
    public static readonly List<string> KnownValues = [
        PromptValues.None, PromptValues.Login, PromptValues.Consent, PromptValues.SelectAccount,
    ];
}

/// <summary>
///     Validates the <c>prompt</c> and <c>max_age</c> parameters of an authorization request, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
///         OpenID Connect Core 1.0
///         §3.1.2.1: Authentication Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     The <c>prompt=none</c> value must not be combined with other prompt values, and requires an
///     existing authenticated session. If <c>login</c> or <c>select_account</c> is present,
///     <see cref="AuthorizeContext{TApp}.RequireReauthentication" /> is set. The <c>max_age</c>
///     parameter (OpenID Connect Core 1.0 §2) triggers reauthentication when the last auth_time
///     exceeds the specified age; the read comes from the
///     <see cref="IAuthenticationContextProvider" />-resolved context. Without a host-supplied
///     provider no auth_time evidence exists and every <c>max_age</c> request reauthenticates.
///     The registered <c>require_auth_time</c> flag (OpenID Connect Dynamic Client
///     Registration 1.0 §2) is enforced independently of <c>max_age</c>: an authenticated
///     session without auth_time evidence reauthenticates so the issued ID token can carry
///     the claim.
///     <para>
///         <c>acr_values</c> passes through validation untouched: the parameter requests the
///         <c>acr</c> claim as a Voluntary Claim (§3.1.2.1), and §5.5.1.1 directs an OP that
///         cannot provide a requested value to return the session's current <c>acr</c> — an
///         unsatisfiable request is never an error. Only the essential
///         <c>claims</c>-parameter form of the request carries MUST-reject semantics, and that
///         form is not implemented. Satisfying the request happens where the authentication is
///         performed: the login pipeline resolves the requested values against the class it
///         achieved and stamps the <c>acr</c> claim accordingly.
///     </para>
/// </remarks>
/// <seealso cref="AdviceAuthorizeConsent{TApp, TAuth}" />
public sealed class AdviceAuthorizePrompt<TApp>(
    IAuthenticationContextProvider? contexts = null,
    TimeProvider?                   time     = null,
    IOptions<SchemataAuthorizationOptions>? options = null
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    private readonly IAuthenticationContextProvider? _contexts = contexts;
    private readonly TimeProvider                   _time     = time ?? TimeProvider.System;

    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizePrompt.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        // Registered authentication defaults (OIDC Registration §2): default_max_age and
        // default_acr_values apply when the request omits the parameter; an explicit request
        // always wins. Applied here - before the PAR shortcut and the max_age handling below -
        // so the whole existing pipeline enforces the defaulted value.
        if (string.IsNullOrWhiteSpace(authz.Request?.MaxAge)
         && !string.IsNullOrWhiteSpace(authz.Application?.DefaultMaxAge)) {
            authz.Request!.MaxAge = authz.Application.DefaultMaxAge;
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.AcrValues)
         && authz.Application?.DefaultAcrValues is { Count: > 0 }) {
            authz.Request!.AcrValues = string.Join(' ', authz.Application.DefaultAcrValues);
        }

        var none  = false;
        var login = false;

        if (!string.IsNullOrWhiteSpace(authz.Request?.Prompt)) {
            var values = authz.Request.Prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var v in values) {
                if (!AdviceAuthorizePrompt.KnownValues.Contains(v, StringComparer.Ordinal)) {
                    throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = v });
                }
            }

            none  = values.Contains(PromptValues.None);
            login = values.Contains(PromptValues.Login);

            switch (none) {
                case true when values.Length > 1:
                    throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_PROMPT_COMBINATION, new Dictionary<string, string?> { ["value"] = PromptValues.None });
                case true when authz.Stage != AuthorizationRequestStage.Pushed && authz.Principal?.Identity?.IsAuthenticated != true:
                    throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
            }

            if (login || values.Contains(PromptValues.SelectAccount)) {
                authz.RequireReauthentication = true;
            }
        }

        int? age = null;
        if (!string.IsNullOrWhiteSpace(authz.Request?.MaxAge)) {
            if (!int.TryParse(authz.Request.MaxAge, out var parsed) || parsed < 0) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.MaxAge });
            }

            age = parsed;
        }

        if (authz.Stage == AuthorizationRequestStage.Pushed) {
            return AdviseResult.Continue;
        }

        var sid = authz.SessionId;
        var context = await AuthenticationContextExtensions.ResolveAsync(
            authz.Principal,
            authz.Authentication,
            sid,
            options?.Value.SessionIdClaimType ?? Claims.SessionId,
            _contexts,
            ct);
        authz.Authentication = context;

        // require_auth_time (OIDC Registration §2): the ID token must carry auth_time, whether or
        // not the request also constrains max_age. Evidence comes from the authentication context
        // - never fabricated - so a session without auth_time evidence reauthenticates instead of
        // minting a token lacking the claim; prompt=none cannot interact and fails instead.
        if (authz.Application?.RequireAuthTime == true
         && authz.Principal?.Identity?.IsAuthenticated == true
         && context?.AuthTime is null) {
            if (none) {
                throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
            }

            authz.RequireReauthentication = true;
        }

        if (age is null) {
            return AdviseResult.Continue;
        }

        if (context?.AuthTime is { } epoch) {
            var time = DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (_time.GetUtcNow() - time <= TimeSpan.FromSeconds(age.Value)) {
                return AdviseResult.Continue;
            }
        }

        if (none) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }

        authz.RequireReauthentication = true;

        return AdviseResult.Continue;
    }

    #endregion
}
