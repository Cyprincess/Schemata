using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Messaging.Skeleton;

public sealed class StreamExecutionContext(AdviceContext advice, ClaimsPrincipal? principal)
{
    public AdviceContext Advice { get; } = advice;
    public ClaimsPrincipal? Principal { get; } = principal;
}
