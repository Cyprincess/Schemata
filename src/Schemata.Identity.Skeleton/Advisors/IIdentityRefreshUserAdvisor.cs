using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises the user loaded during a refresh-token operation.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IIdentityRefreshUserAdvisor<TUser> : IAdvisor<TUser>
    where TUser : SchemataUser;
