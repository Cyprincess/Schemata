using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceCodeExchangePkce
{
    public const int DefaultOrder = AdviceCodeExchangeValidation.DefaultOrder + 10_000_000;
}

public sealed class AdviceCodeExchangePkce<TApp, TToken>(IOptions<CodeFlowOptions> options) : ICodeExchangeAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region ICodeExchangeAdvisor<TApp,TToken> Members

    public int Order => AdviceCodeExchangePkce.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        CodeExchangeContext<TApp, TToken> exchange,
        CancellationToken                 ct = default
    ) {
        if (string.IsNullOrWhiteSpace(exchange.Payload?.CodeChallenge)) {
            if (!string.IsNullOrWhiteSpace(exchange.Request?.CodeVerifier)
             && options.Value.RequirePkceDowngradeProtection) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4005));
            }

            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(exchange.Request?.CodeVerifier)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4005));
        }

        if (exchange.Request.CodeVerifier.Length is < 43 or > 128 || !exchange.Request.CodeVerifier.All(IsUnreserved)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4005));
        }

        var valid = exchange.Payload.CodeChallengeMethod switch {
            PkceMethods.S256 => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(exchange.Request.CodeVerifier))) == exchange.Payload.CodeChallenge,
            PkceMethods.Plain when !options.Value.RequirePkceS256 => exchange.Request.CodeVerifier == exchange.Payload.CodeChallenge,
            var _ => false,
        };

        if (!valid) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4005));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    // RFC 7636 §4.1: unreserved = ALPHA / DIGIT / "-" / "." / "_" / "~"
    private static bool IsUnreserved(char c) {
        return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~';
    }
}
