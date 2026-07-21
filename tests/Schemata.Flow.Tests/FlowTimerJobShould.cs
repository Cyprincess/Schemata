using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Scheduling.Internal;
using Schemata.Scheduling.Skeleton;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowTimerJobShould
{
    [Fact]
    public async SystemTask MissingProcessName_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(
            new() { Variables = new Dictionary<string, string?>() }, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingTokenName_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        var context = new JobContext {
            Variables = new Dictionary<string, string?> { ["processName"] = "processes/p1" },
        };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingTimerDefinition_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        var context = new JobContext {
            Variables = new Dictionary<string, string?> {
                ["processName"] = "processes/p1",
                ["tokenName"]   = "processes/p1/tokens/t1",
            },
        };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }
}
