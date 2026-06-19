using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises account recovery operations for identity users.
/// </summary>
public interface IIdentityRecoveryAdvisor : IAdvisor<SchemataUser, IdentityOperation>;
