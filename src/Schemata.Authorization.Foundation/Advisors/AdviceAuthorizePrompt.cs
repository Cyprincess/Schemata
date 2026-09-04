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
    TimeProvider?                   time     = null
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
        var none  = false;
        var login = false;

        if (!string.IsNullOrWhiteSpace(authz.Request?.Prompt)) {
            var values = authz.Request.Prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var v in values) {
                if (!AdviceAuthorizePrompt.KnownValues.Contains(v, StringComparer.Ordinal)) {
                    throw new OAuthException(
                        OAuthErrors.InvalidRequest,
                        string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), v)
                    );
                }
            }

            none  = values.Contains(PromptValues.None);
            login = values.Contains(PromptValues.Login);

            switch (none) {
                case true when values.Length > 1:
                    throw new OAuthException(
                        OAuthErrors.InvalidRequest,
                        string.Format(SchemataResources.GetResourceString(SchemataResources.INVALID_PROMPT_COMBINATION), PromptValues.None)
                    );
                case true when (authz.Principal?.Identity?.IsAuthenticated != true):
                    throw new OAuthException(
                        OAuthErrors.LoginRequired,
                        SchemataResources.GetResourceString(SchemataResources.USER_AUTHENTICATION_REQUIRED)
                    );
            }

            if (login || values.Contains(PromptValues.SelectAccount)) {
                authz.RequireReauthentication = true;
            }
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.MaxAge)) {
            return AdviseResult.Continue;
        }

        if (!int.TryParse(authz.Request.MaxAge, out var age) || age < 0) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.MaxAge)
            );
        }

        var context = _contexts is null ? null : await _contexts.GetContextAsync(authz.Principal, ct);
        if (context?.AuthTime is { } epoch) {
            var time = DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (_time.GetUtcNow() - time <= TimeSpan.FromSeconds(age)) {
                return AdviseResult.Continue;
            }
        }

        if (none) {
            throw new OAuthException(
                OAuthErrors.LoginRequired,
                SchemataResources.GetResourceString(SchemataResources.USER_AUTHENTICATION_REQUIRED)
            );
        }

        authz.RequireReauthentication = true;

        return AdviseResult.Continue;
    }

    #endregion
}
