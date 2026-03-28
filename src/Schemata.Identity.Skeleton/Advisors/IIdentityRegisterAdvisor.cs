using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityRegisterAdvisor<TUser> : IAdvisor<TUser, RegisterRequest>
    where TUser : SchemataUser;
