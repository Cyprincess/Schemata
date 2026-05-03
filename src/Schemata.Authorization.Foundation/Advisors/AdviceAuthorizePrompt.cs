using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants and known prompt values for <see cref="AdviceAuthorizePrompt{TApp}" />.</summary>
public static class AdviceAuthorizePrompt
{
    public const int DefaultOrder = AdviceAuthorizeNonce.DefaultOrder + 10_000_000;

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
///     exceeds the specified age.
/// </remarks>
/// <seealso cref="AdviceAuthorizeConsent{TApp, TAuth}" />
public sealed class AdviceAuthorizePrompt<TApp> : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizePrompt.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
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
                    throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), v));
                }
            }

            none  = values.Contains(PromptValues.None);
            login = values.Contains(PromptValues.Login);

            switch (none) {
                case true when values.Length > 1:
                    throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST4015), PromptValues.None));
                case true when (authz.Principal?.Identity?.IsAuthenticated != true):
                    throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.GetResourceString(SchemataResources.ST4011));
            }

            if (login || values.Contains(PromptValues.SelectAccount)) {
                authz.RequireReauthentication = true;
            }
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.MaxAge)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!int.TryParse(authz.Request.MaxAge, out var age) || age < 0) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.MaxAge));
        }

        var at = authz.Principal?.FindFirstValue(Claims.AuthTime);
        if (!string.IsNullOrWhiteSpace(at) && long.TryParse(at, out var epoch)) {
            var time = DateTimeOffset.FromUnixTimeSeconds(epoch);
            if (DateTimeOffset.UtcNow - time <= TimeSpan.FromSeconds(age)) {
                return Task.FromResult(AdviseResult.Continue);
            }
        }

        if (none) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.GetResourceString(SchemataResources.ST4011));
        }

        authz.RequireReauthentication = true;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
