using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises profile change operations for identity users.
/// </summary>
public interface IIdentityProfileChangeAdvisor : IAdvisor<SchemataUser, IdentityOperation>;
