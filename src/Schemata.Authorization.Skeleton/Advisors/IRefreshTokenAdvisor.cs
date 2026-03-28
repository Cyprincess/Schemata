using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

public interface IRefreshTokenAdvisor<TApplication, TToken> : IAdvisor<RefreshTokenContext<TApplication, TToken>>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
