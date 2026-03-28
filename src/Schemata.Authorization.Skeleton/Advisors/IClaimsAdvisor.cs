using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Authorization.Skeleton.Advisors;

public interface IClaimsAdvisor : IAdvisor<List<Claim>>;
