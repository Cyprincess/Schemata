using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>Throws from its before segment, so the dispatcher surfaces the advisor's own exception.</summary>
public sealed class ThrowingRenameAdvisor(Exception error) : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => 0;

    public Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        throw error;
    }
}