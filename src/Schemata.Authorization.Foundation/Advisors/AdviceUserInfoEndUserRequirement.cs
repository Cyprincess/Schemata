using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Requires trusted end-user provenance for Profile and UserInfo responses.</summary>
/// <seealso cref="Features.UserInfoFeature" />
public sealed class AdviceUserInfoEndUserRequirement : IUserInfoAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;

    #region IUserInfoAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, UserInfoContext info, CancellationToken ct = default) {
        if (!info.IsEndUserToken || string.IsNullOrWhiteSpace(info.InternalSubject)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.USER_IDENTITY_REQUIRED, code: 403);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
