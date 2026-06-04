using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityProfileResponseAdvisor<TUser> : IAdvisor<TUser, ClaimsStore, ClaimsPrincipal>
    where TUser : SchemataUser;
