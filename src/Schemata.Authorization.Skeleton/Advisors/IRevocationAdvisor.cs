using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

public interface IRevocationAdvisor<TApplication, TToken> : IAdvisor<TApplication, RevokeRequest, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
