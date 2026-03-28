using System;
using System.Security.Claims;
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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceAuthorizeConsent
{
    public const int DefaultOrder = AdviceAuthorizePrompt.DefaultOrder + 10_000_000;
}

public sealed class AdviceAuthorizeConsent<TApp, TAuth>(IAuthorizationManager<TAuth> authorizations) : IAuthorizeAdvisor<TApp>
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
        var prompts = authz.Request?.Prompt?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var consent = prompts.Contains(PromptValues.Consent);
        var none    = prompts.Contains(PromptValues.None);

        var subject = authz.Principal?.FindFirstValue(Claims.Subject);

        var scopes = ScopeParser.Parse(authz.Request?.Scope);

        var authorized = false;
        if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(authz.Application?.Name)) {
            await foreach (var a in authorizations.ListAsync(subject, authz.Application.Name, ct)) {
                if (a.Status != TokenStatuses.Valid) {
                    continue;
                }

                if (a.Type is not (AuthorizationTypes.AdHoc or AuthorizationTypes.Permanent)) {
                    continue;
                }

                var granted = ScopeParser.Parse(a.Scopes);

                if (scopes.IsSubsetOf(granted)) {
                    authorized = true;
                    break;
                }
            }
        }

        switch (authz.Application?.ConsentType) {
            case ConsentTypes.External:
                if (consent) {
                    throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST4016), PromptValues.Consent));
                }

                if (!authorized) {
                    throw new OAuthException(OAuthErrors.ConsentRequired, SchemataResources.GetResourceString(SchemataResources.ST4010));
                }

                authz.ConsentDecision = ConsentDecision.Granted;
                return AdviseResult.Continue;

            case ConsentTypes.Implicit:
                if (consent) {
                    authz.ConsentDecision = ConsentDecision.Required;
                    return AdviseResult.Continue;
                }

                authz.ConsentDecision = ConsentDecision.Granted;
                return AdviseResult.Continue;

            case ConsentTypes.Explicit:
            default:
                if (authorized) {
                    if (consent) {
                        authz.ConsentDecision = ConsentDecision.Required;
                        return AdviseResult.Continue;
                    }

                    authz.ConsentDecision = ConsentDecision.Granted;
                    return AdviseResult.Continue;
                }

                if (none) {
                    throw new OAuthException(OAuthErrors.ConsentRequired, SchemataResources.GetResourceString(SchemataResources.ST4010));
                }

                authz.ConsentDecision = ConsentDecision.Required;
                return AdviseResult.Continue;
        }
    }

    #endregion
}
