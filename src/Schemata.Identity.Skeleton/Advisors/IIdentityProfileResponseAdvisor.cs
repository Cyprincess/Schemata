using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityProfileResponseAdvisor<TUser> : IAdvisor<TUser, IList<Claim>, ClaimsPrincipal>
    where TUser : SchemataUser;
