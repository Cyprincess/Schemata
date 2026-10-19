using System.Collections.Generic;
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
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizePkce{TApp}" />.</summary>
public static class AdviceAuthorizePkce
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeResource.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates PKCE (Proof Key for Code Exchange) parameters during authorization,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.2">
///         RFC 7636: Proof Key for Code Exchange by
///         OAuth Public Clients §4.2: Client Creates the Code Challenge
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7636.html#section-4.3">
///         RFC 7636: Proof Key for Code Exchange by
///         OAuth Public Clients §4.3: Client Sends the Code Challenge with the Authorization Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     When PKCE is required at the server level via
///     <see cref="CodeFlowOptions.RequirePkce" />, the <c>code_challenge</c> must be present.
///     If <c>code_challenge_method</c> is omitted, <c>plain</c> is assumed per RFC 7636.
///     When <see cref="CodeFlowOptions.RequirePkceS256" /> is true, only <c>S256</c> is accepted.
/// </remarks>
/// <seealso cref="CodeFlowOptions" />
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

        // An absent or empty challenge is only a violation when PKCE is required. A present
        // value — including whitespace-only input that older IsNullOrWhiteSpace checks erased —
        // must satisfy the RFC 7636 §4.2 grammar either way, so malformed-present input is
        // reported as a grammar failure rather than folded into the required/absent fact.
        if (required && string.IsNullOrEmpty(authz.Request?.CodeChallenge)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.NOT_EMPTY,
                new Dictionary<string, string?> { ["value"] = Parameters.CodeChallenge }
            );
        }

        if (string.IsNullOrEmpty(authz.Request?.CodeChallenge)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!Pkce.IsValid(authz.Request.CodeChallenge)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.CODE_CHALLENGE_INVALID
            );
        }

        // Normalize a missing method to "plain" per RFC 7636 §4.3, and write the result back to
        // the request so every downstream path (silent auto-approval, consent UI, code persistence)
        // sees the same canonical value. Without this, AdviceCodeExchangePkce later receives a null
        // method and rejects the otherwise-valid exchange.
        var method = authz.Request.CodeChallengeMethod;
        if (string.IsNullOrWhiteSpace(method)) {
            method                            = PkceMethods.Plain;
            authz.Request.CodeChallengeMethod = method;
        }

        switch (method) {
            case PkceMethods.S256:
            case PkceMethods.Plain when !options.Value.RequirePkceS256:
                break;
            case PkceMethods.Plain:
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.CODE_CHALLENGE_METHOD_NOT_ALLOWED,
                    new Dictionary<string, string?> { ["value"] = PkceMethods.Plain }
                );
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.NOT_SUPPORTED,
                    new Dictionary<string, string?> { ["value"] = method }
                );
        }


        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
