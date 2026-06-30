using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowResourceMethodHandlerShould
{
    [Fact]
    public async Task Start_RejectsAnonymous_WhenDefinitionRequiresAuthorization() {
        var handler = new StartProcessHandler(Runner(Registry("approval", true).Object));

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await handler.InvokeAsync(null, new() { DefinitionName = "approval" }, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Start_FailsPrecondition_WhenEngineNotRegistered() {
        var handler = new StartProcessHandler(Runner(Registry("approval", false).Object));

        // The definition resolves but no IFlowRuntime is registered under the "StateMachine" key.
        await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await handler.InvokeAsync(null, new() { DefinitionName = "approval" }, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Complete_RejectsAnonymous_WhenProcessRequiresAuthorization() {
        var handler = new CompleteActivityHandler(Runner(Registry("approval", true).Object));
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await handler.InvokeAsync("processes/p1", new(), process, null, CancellationToken.None));
    }

    [Fact]
    public async Task Correlate_RejectsAnonymous_WhenProcessRequiresAuthorization() {
        var handler = new CorrelateMessageHandler(Runner(Registry("approval", true).Object));
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await handler.InvokeAsync("processes/p1", new() { MessageName = "approve" }, process, null, CancellationToken.None));
    }

    [Fact]
    public async Task Terminate_RejectsAnonymous_WhenProcessRequiresAuthorization() {
        var handler = new TerminateProcessHandler(Runner(Registry("approval", true).Object));
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await handler.InvokeAsync("processes/p1", new(), process, null, CancellationToken.None));
    }

    [Fact]
    public async Task Signal_RejectsAnonymous_WhenAnyListeningProcessRequiresAuthorization() {
        var handler = new ThrowSignalHandler(Runner(Registry("approval", true, "approved").Object));

        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await handler.InvokeAsync(null, new() { SignalName = "approved" }, null, null, CancellationToken.None));
    }

    private static ProcessPersistence Persistence() {
        return new();
    }

    private static FlowRunner Runner(IProcessRegistry registry) {
        return new(registry, new(), Persistence(), Notifier(), new ServiceCollection().BuildServiceProvider());
    }

    private static ProcessLifecycleNotifier Notifier() {
        return new(
            [],
            [],
            new NullLogger<ProcessLifecycleNotifier>());
    }

    private static Mock<IProcessRegistry> Registry(
        string  processName,
        bool    requiresAuthorization,
        string? signalName = null
    ) {
        var definition = new ProcessDefinition { Name = processName };
        if (signalName is not null) {
            definition.Signals.Add(new() { Name = signalName });
        }

        var registration = new ProcessRegistration {
            Name          = processName,
            Engine        = "StateMachine",
            Definition    = definition,
            Configuration = new() { Name = processName, RequiresAuthorization = requiresAuthorization },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegistration(processName)).Returns(registration);
        registry.Setup(r => r.GetRegisteredProcesses()).Returns([processName]);
        return registry;
    }
}
