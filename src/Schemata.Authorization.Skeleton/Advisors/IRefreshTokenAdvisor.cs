using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked during refresh token exchange at the token endpoint.
/// </summary>
public interface IRefreshTokenAdvisor<TApplication, TToken> : IAdvisor<RefreshTokenContext<TApplication, TToken>>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
