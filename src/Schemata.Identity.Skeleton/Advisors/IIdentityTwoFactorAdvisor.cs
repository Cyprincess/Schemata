using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises two-factor authentication operations for identity users.
/// </summary>
public interface IIdentityTwoFactorAdvisor : IAdvisor<SchemataUser, IdentityOperation>;
