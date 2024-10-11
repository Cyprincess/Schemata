using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Models;
using Xunit;

namespace Schemata.Workflow.Tests.TestControllers;

public class TestSubmit
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Submit() {
        var instance = new Order();

        var request = new WorkflowRequest<IStateful> {
            Type = instance.GetType().FullName,
            Instance = instance,
        };

        var (controller, _) = _fixture.CreateWorkflowController();
        var result = await controller.Submit(request);

        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<WorkflowResponse>(ok.Value);
        Assert.Equal(nameof(OrderStateMachine.Initial), response.State);

        var entity = _fixture.Orders.LastOrDefault();
        Assert.Equal(response.Id, entity?.Id);
        Assert.Equal(response.State, entity?.State);
    }
}
