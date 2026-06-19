using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises the claims returned with an identity profile response.
/// </summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public interface IIdentityProfileResponseAdvisor<TUser> : IAdvisor<TUser, ClaimsStore, ClaimsPrincipal>
    where TUser : SchemataUser;
