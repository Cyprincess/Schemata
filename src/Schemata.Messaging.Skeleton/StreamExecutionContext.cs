using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Messaging.Skeleton;

public sealed class StreamExecutionContext(AdviceContext advice, TenantIdentity identity, ClaimsPrincipal? principal)
{
    public AdviceContext Advice { get; } = advice;
    public TenantIdentity Identity { get; } = identity;
    public ClaimsPrincipal? Principal { get; } = principal;
}
