using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceIntrospectionTokenValidation{TApp, TToken}" />.</summary>
public static class AdviceIntrospectionTokenValidation
{
    public const int DefaultOrder = AdviceIntrospectionProtectedResource.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates that the token being introspected is an access token or refresh token in valid status,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.1">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.1: Introspection Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <typeparam name="TToken">The token entity type.</typeparam>
/// <seealso cref="AdviceIntrospectionProtectedResource{TApp, TToken}" />
public sealed class AdviceIntrospectionTokenValidation<TApp, TToken> : IIntrospectionAdvisor<TApp, TToken>
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IIntrospectionAdvisor<TApp,TToken> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceIntrospectionTokenValidation.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp, TToken> introspection,
        CancellationToken                  ct = default
    ) {
        var token = introspection.Token;

        if (token?.Type != TokenTypes.AccessToken && token?.Type != TokenTypes.RefreshToken) {
            return Task.FromResult(AdviseResult.Block);
        }

        if (token.Status != TokenStatuses.Valid) {
            return Task.FromResult(AdviseResult.Block);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
