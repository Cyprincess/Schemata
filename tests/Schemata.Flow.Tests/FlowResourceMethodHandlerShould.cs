using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowResourceMethodHandlerShould
{
    [Fact]
    public async Task Start_FailsPrecondition_WhenEngineNotRegistered() {
        var handler = new StartProcessHandler(Runner(Registry("approval").Object));

        await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await handler.InvokeAsync(null, new() { DefinitionName = "approval" }, null, null, CancellationToken.None));
    }

    private static FlowRunner Runner(IProcessRegistry registry) {
        return new(registry, new(), Notifier(), new ServiceCollection().BuildServiceProvider());
    }

    private static ProcessLifecycleNotifier Notifier() {
        return new(
            [],
            [],
            new NullLogger<ProcessLifecycleNotifier>());
    }

    private static Mock<IProcessRegistry> Registry(string processName) {
        var registration = new ProcessRegistration {
            Name          = processName,
            Engine        = "StateMachine",
            Definition    = new() { Name = processName },
            Configuration = new() { Name = processName },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration(processName)).Returns(registration);
        return registry;
    }
}
