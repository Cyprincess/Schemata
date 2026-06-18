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

/// <summary>Order constants for <see cref="AdviceAuthorizePkce{TApp}" />.</summary>
public static class AdviceAuthorizePkce
{
    public const int DefaultOrder = AdviceAuthorizeScopeValidation.DefaultOrder + 10_000_000;
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
///     When PKCE is required (either at the server level via
///     <see cref="CodeFlowOptions.RequirePkce" /> or the application level via
///     <see cref="SchemataApplication.RequirePkce" />), the <c>code_challenge</c> must be present.
///     If <c>code_challenge_method</c> is omitted, <c>plain</c> is assumed per RFC 7636.
///     When <see cref="CodeFlowOptions.RequirePkceS256" /> is true, only <c>S256</c> is accepted.
/// </remarks>
/// <seealso cref="CodeFlowOptions" />
/// <seealso cref="AdviceCodeExchangePkce{TApp, TToken}" />
public sealed class AdviceAuthorizePkce<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
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
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.CodeChallenge)
            );
        }

        if (string.IsNullOrWhiteSpace(authz.Request?.CodeChallenge)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!IsValidCodeChallenge(authz.Request.CodeChallenge)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.CodeChallenge)
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
                    string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), PkceMethods.Plain)
                );
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    string.Format(
                        SchemataResources.GetResourceString(SchemataResources.ST1015),
                        Parameters.CodeChallengeMethod
                    )
                );
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    // RFC 7636 §4.1/§4.2: a code challenge is 43-128 characters drawn from the unreserved set
    // [A-Za-z0-9-._~]. Rejecting a malformed value here surfaces the fault at the authorize step
    // instead of as an opaque verifier mismatch during the later code exchange.
    private static bool IsValidCodeChallenge(string value) {
        if (value.Length is < 43 or > 128) {
            return false;
        }

        foreach (var c in value) {
            var valid = c is >= 'A' and <= 'Z'
                     or >= 'a' and <= 'z'
                     or >= '0' and <= '9'
                     or '-' or '.' or '_' or '~';
            if (!valid) {
                return false;
            }
        }

        return true;
    }
}
