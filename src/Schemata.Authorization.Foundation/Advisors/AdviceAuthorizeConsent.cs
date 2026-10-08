using System.Collections.Generic;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeConsent{TApp, TAuth}" />.</summary>
public static class AdviceAuthorizeConsent
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizePrompt.DefaultOrder + 10_000_000;
}

/// <summary>
///     Makes the consent decision from the <see cref="ConsentModel" /> resolved by the registered
///     <see cref="IConsentModelProvider" />, the prompt parameter, and any prior authorization, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
///         OpenID Connect Core 1.0
///         §3.1.2.1: Authentication Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TAuth">The authorization entity type.</typeparam>
/// <remarks>
///     For <see cref="ConsentModel.Explicit" />, an existing authorization is itself sufficient — unlike implicit,
///     which always grants. The <c>prompt=none</c> value triggers <c>consent_required</c> when no prior
///     consent exists.
/// </remarks>
/// <seealso cref="AdviceAuthorizePrompt" />
/// <seealso cref="AdviceAuthorizeAutoApproveSignIn{TApp, TAuth}" />
public sealed class AdviceAuthorizeConsent<TApp, TAuth>(
    IAuthorizationManager<TAuth> authorizations,
    IConsentModelProvider        consent,
    AuthorizationDetailsService? details = null
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeConsent.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (authz.Stage == AuthorizationRequestStage.Pushed) {
            return AdviseResult.Continue;
        }

        var prompts = authz.Request?.Prompt?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var consentPrompt = prompts.Contains(PromptValues.Consent);
        var none    = prompts.Contains(PromptValues.None);

        var subject = authz.Principal?.FindFirstValue(IdentityClaims.Subject);

        var scopes = ScopeParser.Parse(authz.Request?.Scope);

        var authorized = false;
        if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(authz.Application?.CanonicalName)) {
            await foreach (var a in authorizations.ListAsync(subject, authz.Application.CanonicalName, ct)) {
                if (a.Status != TokenStatuses.Valid) {
                    continue;
                }

                if (a.Type is not (AuthorizationTypes.AdHoc or AuthorizationTypes.Permanent)) {
                    continue;
                }

                var granted = ScopeParser.Parse(a.Scopes);
                if (!scopes.IsSubsetOf(granted)) {
                    continue;
                }

                if (authz.Request?.RedirectUri != a.RedirectUri) {
                    continue;
                }

                if (!ScopeParser.IsSubset(authz.Request?.ResponseType, a.ResponseType)) {
                    continue;
                }

                if (authz.Request?.CodeChallengeMethod != a.CodeChallengeMethod) {
                    continue;
                }

                if (!ScopeParser.IsSubset(authz.Request?.AcrValues, a.AcrValues)) {
                    continue;
                }
                if (details is not null) {
                    // RFC 9396 §6.1: prior consent covers the current request only when every
                    // requested detail narrows from the granted set under the type descriptor's
                    // own comparison; an expanded or different set must take the consent path.
                    try {
                        details.Narrow(a.AuthorizationDetails, authz.AuthorizationDetails, ct);
                    } catch (OAuthException) {
                        continue;
                    }
                }


                authorized = true;

                break;
            }
        }

        switch (consent.Resolve(authz.Application, authz.Request)) {
            case ConsentModel.External:
                if (consentPrompt) {
                    throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.UNSUPPORTED_PROMPT, new Dictionary<string, string?> { ["value"] = PromptValues.Consent });
                }

                if (!authorized) {
                    throw new OAuthException(OAuthErrors.ConsentRequired, SchemataResources.USER_CONSENT_REQUIRED);
                }

                authz.ConsentDecision = ConsentDecision.Granted;
                return AdviseResult.Continue;

            case ConsentModel.Implicit:
                if (consentPrompt) {
                    authz.ConsentDecision = ConsentDecision.Required;
                    return AdviseResult.Continue;
                }

                authz.ConsentDecision = ConsentDecision.Granted;
                return AdviseResult.Continue;

            case ConsentModel.Explicit:
            default:
                if (authorized) {
                    if (consentPrompt) {
                        authz.ConsentDecision = ConsentDecision.Required;
                        return AdviseResult.Continue;
                    }

                    authz.ConsentDecision = ConsentDecision.Granted;
                    return AdviseResult.Continue;
                }

                if (none) {
                    throw new OAuthException(OAuthErrors.ConsentRequired, SchemataResources.USER_CONSENT_REQUIRED);
                }

                authz.ConsentDecision = ConsentDecision.Required;
                return AdviseResult.Continue;
        }
    }

    #endregion
}
