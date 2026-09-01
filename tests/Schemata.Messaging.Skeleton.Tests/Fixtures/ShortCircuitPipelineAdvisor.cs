using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>Returns its own value without calling the continuation, proving the short-circuit path.</summary>
public sealed class ShortCircuitPipelineAdvisor(string value) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        return Task.FromResult(value);
    }
}