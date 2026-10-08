using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Flow.Skeleton;

/// <summary>Resolves a canonical actor and application-verified group references.</summary>
public interface IFlowSubjectResolver
{
    ValueTask<FlowSubjects> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public sealed record FlowSubjects(string? Actor, IReadOnlyCollection<string> Groups);
