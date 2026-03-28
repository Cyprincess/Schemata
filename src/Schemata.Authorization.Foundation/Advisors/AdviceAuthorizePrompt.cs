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

public static class AdviceAuthorizePrompt
{
    public const int DefaultOrder = AdviceAuthorizeNonce.DefaultOrder + 10_000_000;

    public static readonly List<string> KnownValues = [
        PromptValues.None, PromptValues.Login, PromptValues.Consent, PromptValues.SelectAccount,
    ];
}

public sealed class AdviceAuthorizePrompt<TApp> : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizePrompt.DefaultOrder;

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
