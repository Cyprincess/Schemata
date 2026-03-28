using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityRefreshUserAdvisor<TUser> : IAdvisor<TUser>
    where TUser : SchemataUser;
