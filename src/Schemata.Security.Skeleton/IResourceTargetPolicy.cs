using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Security.Skeleton;

/// <summary>Domain-owned admission before instance loading and row entitlement evaluation.</summary>
public interface IResourceTargetPolicy
{
    Task<AccessDecision> EvaluateAsync(ResourceTarget target, ClaimsPrincipal? principal, CancellationToken ct = default);
}
