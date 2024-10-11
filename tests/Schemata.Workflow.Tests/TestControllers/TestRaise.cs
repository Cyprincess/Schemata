using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Workflow.Skeleton.Models;
using Xunit;

namespace Schemata.Workflow.Tests.TestControllers;

public class TestRaise
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Raise() {
        var workflow = _fixture.Workflows.First();

        var (controller, _) = _fixture.CreateWorkflowController();
        var result = await controller.Raise(workflow.Id, new OrderEvent { Event = nameof(OrderStateMachine.Pay) });

        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<WorkflowResponse>(ok.Value);
        Assert.Equal(workflow.Id, response.Id);
        Assert.Equal(nameof(OrderStateMachine.Progressing), response.State);
    }
}
