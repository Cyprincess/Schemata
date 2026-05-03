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

/// <summary>Order constants for <see cref="AdviceAuthorizeNonce{TApp}" />.</summary>
public static class AdviceAuthorizeNonce
{
    public const int DefaultOrder = AdviceAuthorizePkce.DefaultOrder + 10_000_000;
}

/// <summary>
///     Requires a <c>nonce</c> parameter when the response_type includes <c>id_token</c>, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#AuthRequest">
///         OpenID Connect Core 1.0
///         §3.1.2.1: Authentication Request
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     The nonce mitigates replay attacks on the implicit/hybrid flows by binding the ID Token
///     to the client's session. This check is only enforced when
///     <see cref="CodeFlowOptions.RequireNonce" /> is set.
/// </remarks>
/// <seealso cref="CodeFlowOptions" />
public sealed class AdviceAuthorizeNonce<TApp>(IOptions<CodeFlowOptions> options) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceAuthorizeNonce.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (options.Value.RequireNonce
         && !string.IsNullOrWhiteSpace(authz.Request?.ResponseType)
         && authz.Request.ResponseType.Contains(ResponseTypes.IdToken)
         && string.IsNullOrWhiteSpace(authz.Request.Nonce)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.Nonce));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
