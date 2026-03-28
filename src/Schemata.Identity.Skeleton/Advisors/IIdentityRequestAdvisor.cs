using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Skeleton.Advisors;

public interface IIdentityRequestAdvisor<in T> : IAdvisor<T, IdentityOperation, ClaimsPrincipal>;
