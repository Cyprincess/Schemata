using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors for the UserInfo endpoint pipeline,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     .
/// </summary>
public interface IUserInfoAdvisor : IAdvisor<UserInfoContext>;
