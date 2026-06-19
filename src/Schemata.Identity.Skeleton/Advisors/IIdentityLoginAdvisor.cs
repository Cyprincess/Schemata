using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises login requests before authentication completes.
/// </summary>
public interface IIdentityLoginAdvisor : IAdvisor<SchemataUser, LoginRequest>;
