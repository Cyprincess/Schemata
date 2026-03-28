using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityRecoveryAdvisor : IAdvisor<SchemataUser, IdentityOperation>;
