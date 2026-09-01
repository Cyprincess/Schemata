using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>Records whether it ran; registered against <see cref="PlainRequest" /> to prove a plain request never runs a chain.</summary>
public sealed class RecordingPlainPipelineAdvisor : IRequestPipelineAdvisor<PlainRequest, string>
{
    public int Order => 0;

    public bool Ran { get; private set; }

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        PlainRequest                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        Ran = true;
        return next(ct);
    }
}