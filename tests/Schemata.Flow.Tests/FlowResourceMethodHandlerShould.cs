using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowResourceMethodHandlerShould
{
    [Fact]
    public async Task Start_DelegatesToRuntime() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", false);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };

        runtime.Setup(r => r.StartProcessInstanceAsync("approval", It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                                                       It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(),
                                                       It.IsAny<string?>(), It.IsAny<object?>(),
                                                       It.IsAny<CancellationToken>()))
               .ReturnsAsync(process);

        var handler = new StartProcessHandler(runtime.Object, registry.Object);

        var result = await handler.InvokeAsync(null, new() { DefinitionName = "approval" }, null, null,
                                               CancellationToken.None);

        Assert.Same(process, result);
        runtime.Verify(
            r => r.StartProcessInstanceAsync("approval", null, null, null, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Start_PassesDisplayNameAndDescription_Atomically() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", false);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };

        runtime.Setup(r => r.StartProcessInstanceAsync(It.IsAny<string>(),
                                                       It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                                                       It.IsAny<ClaimsPrincipal?>(), It.IsAny<string?>(),
                                                       It.IsAny<string?>(), It.IsAny<object?>(),
                                                       It.IsAny<CancellationToken>()))
               .ReturnsAsync(process);

        var handler = new StartProcessHandler(runtime.Object, registry.Object);

        await handler.InvokeAsync(
            null, new() { DefinitionName = "approval", DisplayName = "Approve PR", Description = "Review and approve" },
            null, null, CancellationToken.None);

        // The start call receives metadata and keeps the source entity slot empty.
        runtime.Verify(
            r => r.StartProcessInstanceAsync("approval", null, null, "Approve PR", "Review and approve", null,
                                             It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Complete_DelegatesToRuntime() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", false);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };
        var instance = new ProcessInstance { StateId = "done" };

        runtime.Setup(r => r.CompleteActivityAsync("processes/p1", null, null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(instance);

        var handler = new CompleteActivityHandler(runtime.Object, registry.Object);

        var result = await handler.InvokeAsync("processes/p1", new(), process, null, CancellationToken.None);

        Assert.Same(instance, result);
        Assert.Equal("processes/p1", result.CanonicalName);
    }

    [Fact]
    public async Task Signal_DelegatesToRuntime() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", false, "approved");
        runtime.Setup(r => r.ThrowSignalAsync("approved", null, null, It.IsAny<CancellationToken>()))
               .Returns(ValueTask.CompletedTask);

        var handler = new ThrowSignalHandler(runtime.Object, registry.Object);

        var result = await handler.InvokeAsync(null, new() { SignalName = "approved" }, null, null,
                                               CancellationToken.None);

        Assert.NotNull(result);
        runtime.Verify(r => r.ThrowSignalAsync("approved", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Terminate_DelegatesToRuntime() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", false);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };
        var instance = new ProcessInstance { StateId = "terminated" };

        runtime.Setup(r => r.TerminateProcessInstanceAsync("processes/p1", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(instance);

        var handler = new TerminateProcessHandler(runtime.Object, registry.Object);

        var result = await handler.InvokeAsync("processes/p1", new(), process, null, CancellationToken.None);

        Assert.Same(instance, result);
        Assert.Equal("processes/p1", result.CanonicalName);
    }

    [Fact]
    public async Task Complete_RejectsAnonymous_WhenProcessRequiresAuthorization() {
        var runtime  = new Mock<IProcessRuntime>();
        var registry = Registry("approval", true);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };
        var handler  = new CompleteActivityHandler(runtime.Object, registry.Object);

        await Assert.ThrowsAsync<NotFoundException>(async () => await handler.InvokeAsync(
                                                        "processes/p1", new(), process, new(), CancellationToken.None));

        runtime.Verify(
            r => r.CompleteActivityAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_AllowsAuthenticated_WhenProcessRequiresAuthorization() {
        var runtime = new Mock<IProcessRuntime>();
        var registry = Registry("approval", true);
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        runtime.Setup(r => r.CompleteActivityAsync("processes/p1", null, principal, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ProcessInstance { StateId = "done" });

        var handler = new CompleteActivityHandler(runtime.Object, registry.Object);

        await handler.InvokeAsync("processes/p1", new(), process, principal, CancellationToken.None);

        runtime.Verify(r => r.CompleteActivityAsync("processes/p1", null, principal, It.IsAny<CancellationToken>()),
                       Times.Once);
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
