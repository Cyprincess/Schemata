using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Checks that the UserInfo request includes the <c>openid</c> scope and has a valid internal subject,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     .
/// </summary>
/// <seealso cref="Features.UserInfoFeature" />
public sealed class AdviceUserInfoOpenIdRequirement : IUserInfoAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IUserInfoAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, UserInfoContext info, CancellationToken ct = default) {
        if (!info.GrantedScopes.Contains(Scopes.OpenId)) {
            throw new OAuthException(
                OAuthErrors.InvalidScope,
                SchemataResources.GetResourceString(SchemataResources.ST4006),
                403
            );
        }

        if (string.IsNullOrWhiteSpace(info.InternalSubject)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.ST4017)
            );
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
