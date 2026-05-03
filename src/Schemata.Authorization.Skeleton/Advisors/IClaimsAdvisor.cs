using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors that contribute claims to the final <c>id_token</c> or UserInfo response.
///     Registered advisors are invoked in order; each may add claims to the list.
/// </summary>
public interface IClaimsAdvisor : IAdvisor<List<Claim>>;
