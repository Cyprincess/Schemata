using System.Security.Claims;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowProcessAuthorizationShould
{
    [Fact]
    public void EnsureDefinitionAccess_AllowsAnonymous_WhenDefinitionDoesNotRequireAuthorization() {
        var auth = new FlowProcessAuthorization();
        var ex   = Record.Exception(() => auth.EnsureDefinitionAccess(Registry("approval", false).Object, "approval", null));
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureDefinitionAccess_RejectsAnonymous_WhenDefinitionRequiresAuthorization() {
        var auth = new FlowProcessAuthorization();
        Assert.Throws<NotFoundException>(() => auth.EnsureDefinitionAccess(Registry("approval", true).Object, "approval", null));
    }

    [Fact]
    public void EnsureDefinitionAccess_AllowsAuthenticated_WhenDefinitionRequiresAuthorization() {
        var auth      = new FlowProcessAuthorization();
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var ex        = Record.Exception(() => auth.EnsureDefinitionAccess(Registry("approval", true).Object, "approval", principal));
        Assert.Null(ex);
    }

    [Fact]
    public void EnsureProcessAccess_DelegatesToDefinitionAccess() {
        var auth    = new FlowProcessAuthorization();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "approval" };
        Assert.Throws<NotFoundException>(() => auth.EnsureProcessAccess(Registry("approval", true).Object, process, null));
    }

    [Fact]
    public void EnsureSignalAccess_RejectsAnonymous_WhenAnyListeningProcessRequiresAuthorization() {
        var auth = new FlowProcessAuthorization();
        Assert.Throws<NotFoundException>(() => auth.EnsureSignalAccess(Registry("approval", true, "approved").Object, "approved", null));
    }

    [Fact]
    public void EnsureSignalAccess_AllowsAnonymous_WhenNoListeningProcessRequiresAuthorization() {
        var auth = new FlowProcessAuthorization();
        var ex   = Record.Exception(() => auth.EnsureSignalAccess(Registry("approval", false, "approved").Object, "approved", null));
        Assert.Null(ex);
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
