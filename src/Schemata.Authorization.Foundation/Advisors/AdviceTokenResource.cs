using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceTokenResource" />.</summary>
public static class AdviceTokenResource
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceRequestScopeValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Validates and adopts the <c>resource</c> parameter on access token requests, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2.2">
///         RFC 8707: Resource Indicators for OAuth 2.0 §2.2: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     <para>
///         Every requested value must pass the §2 syntax rules; a malformed value rejects with
///         <c>invalid_target</c>. Grant-specific consistency needs grant state this advisor cannot
///         see: the <c>authorization_code</c> grant compares the requested set with the code payload
///         in <c>AuthorizationCodeHandler</c>, and the <c>refresh_token</c> grant enforces the §2.2
///         subset rule ("...originally granted by the resource owner or a subset thereof") in
///         <c>RefreshTokenHandler</c> against the resources claim of the original grant. Both
///         handlers publish the effective set on the ambient context.
///     </para>
///     <para>
///         Every other grant adopts the requested set directly via
///         <c>ctx.Set(new ResourceIndicators(...))</c> so downstream claim advisors can
///         audience-restrict the issued token.
///     </para>
/// </remarks>
public sealed class AdviceTokenResource<TApp> : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceTokenResource.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        if (request.Resource is not { Count: > 0 }) {
            return Task.FromResult(AdviseResult.Continue);
        }

        foreach (var resource in request.Resource) {
            if (!AdviceAuthorizeResource.IsValidTarget(resource)) {
                throw new OAuthException(
                    OAuthErrors.InvalidTarget,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_TARGET)
                );
            }
        }

        if (request.GrantType is GrantTypes.AuthorizationCode or GrantTypes.RefreshToken) {
            return Task.FromResult(AdviseResult.Continue);
        }

        ctx.Set(new ResourceIndicators([.. request.Resource]));

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
