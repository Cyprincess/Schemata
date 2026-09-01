using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>
///     Records the order of its before segment, the continuation, and its after segment onto a
///     shared trail, and appends a suffix to the response so the after segment's rewrite is
///     observable.
/// </summary>
public sealed class TracingPipelineAdvisor(List<string> trail) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public async Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        trail.Add("before");
        var response = await next(ct);
        trail.Add("after");
        return $"{response}::after";
    }
}