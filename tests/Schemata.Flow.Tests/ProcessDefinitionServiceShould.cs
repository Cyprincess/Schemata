using System.Threading.Tasks;
using Moq;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessDefinitionServiceShould
{
    [Fact]
    public async Task ListDefinitions_PopulatesDisplayNameAndDescription() {
        var definition = new ProcessDefinition {
            Name = "orders", DisplayName = "Orders", Description = "Order fulfilment flow",
        };
        var registration = new ProcessRegistration {
            Name          = "orders",
            Engine        = "StateMachine",
            Definition    = definition,
            Configuration = new() { Name = "orders" },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns(registration);

        var service = new ProcessDefinitionService(registry.Object);

        var result = await service.ListProcessDefinitionsAsync(new());

        var info = Assert.Single(result.Entities!);
        Assert.Equal("definitions/orders", info.CanonicalName);
        Assert.Equal("Orders", info.DisplayName);
        Assert.Equal("Order fulfilment flow", info.Description);
    }
}
