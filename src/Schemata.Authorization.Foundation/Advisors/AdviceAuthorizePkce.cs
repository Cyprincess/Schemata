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
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceAuthorizePkce
{
    public const int DefaultOrder = AdviceAuthorizeScopeValidation.DefaultOrder + 10_000_000;
}

public sealed class AdviceAuthorizePkce<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizePkce.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var required = options.Value.RequirePkce;
        if (authz.Application?.RequirePkce.HasValue == true) {
            required = authz.Application.RequirePkce.Value;
        }

        if (required && string.IsNullOrWhiteSpace(authz.Request?.CodeChallenge)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.CodeChallenge));
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.CodeChallenge)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var method = authz.Request.CodeChallengeMethod;
        if (string.IsNullOrWhiteSpace(method)) {
            method = PkceMethods.Plain;
        }

        switch (method) {
            case PkceMethods.S256:
            case PkceMethods.Plain when !options.Value.RequirePkceS256:
                break;
            case PkceMethods.Plain:
                throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), PkceMethods.Plain));
            default:
                throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.CodeChallengeMethod));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
