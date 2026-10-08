using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Tests.Fixtures;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

public class RequestPipelineAdvisorShould
{
    [Fact]
    public async Task Run_Its_Before_Segment_Then_The_Continuation_Then_Its_After_Segment() {
        var trail = new List<string>();

        var advisor = new TracingPipelineAdvisor(trail);

        var ctx = new AdviceContext(new EmptyServiceProvider());

        var result = await advisor.AdviseAsync(ctx, new("hub"), Continuation, CancellationToken.None);

        Assert.Equal(["before", "handler", "after"], trail);
        Assert.Equal("handler-result::after", result);
        return;

        Task<string> Continuation(CancellationToken _) {
            trail.Add("handler");
            return Task.FromResult("handler-result");
        }
    }

    [Fact]
    public async Task Short_Circuit_Without_Calling_The_Continuation() {
        var trail = new List<string>();

        var advisor = new ShortCircuitPipelineAdvisor("short");

        var ctx = new AdviceContext(new EmptyServiceProvider());

        var result = await advisor.AdviseAsync(ctx, new("hub"), Continuation, CancellationToken.None);

        Assert.Equal("short", result);
        Assert.Empty(trail);
        return;

        Task<string> Continuation(CancellationToken _) {
            trail.Add("handler");
            return Task.FromResult("handler-result");
        }
    }
}
