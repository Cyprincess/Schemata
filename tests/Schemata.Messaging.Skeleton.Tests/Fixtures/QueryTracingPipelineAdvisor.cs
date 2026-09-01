using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>Appends its own tag to a shared trail, so a query pipeline chain running is observable.</summary>
public sealed class QueryTracingPipelineAdvisor(string tag, List<string> trail) : IRequestPipelineAdvisor<CountWidgets, int>
{
    public int Order => 0;

    public Task<int> AdviseAsync(
        AdviceContext                   ctx,
        CountWidgets                    request,
        RequestHandlerContinuation<int> next,
        CancellationToken               ct = default) {
        trail.Add(tag);
        return next(ct);
    }
}