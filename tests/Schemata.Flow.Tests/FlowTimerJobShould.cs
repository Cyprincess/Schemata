using System.Collections.Generic;
using System.Threading;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Scheduling.Internal;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Scheduling.Skeleton;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowTimerJobShould
{
    [Fact]
    public async SystemTask MissingProcessName_Throws() {
        var runtime = new Mock<IProcessRuntime>();
        var job     = new FlowTimerJob(runtime.Object);

        await Assert.ThrowsAsync<FailedPreconditionException>(() =>
            job.ExecuteAsync(new() { Variables = new Dictionary<string, object?>() }, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingTimerDefinition_Throws() {
        var runtime = new Mock<IProcessRuntime>();
        var job     = new FlowTimerJob(runtime.Object);

        var context = new JobContext {
            Variables = new Dictionary<string, object?> { ["processName"] = "processes/p1" },
        };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }
}
