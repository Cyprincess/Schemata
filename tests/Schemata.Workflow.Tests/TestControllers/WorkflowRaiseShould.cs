using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Workflow.Skeleton.Models;
using Xunit;

namespace Schemata.Workflow.Tests.TestControllers;

public class WorkflowRaiseShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Raise_WithValidEvent_ReturnsOkObjectResult() {
        var workflow = _fixture.Workflows.First();

        var (controller, _) = _fixture.CreateWorkflowController();

        var result = await controller.Raise(workflow.Id, new OrderEvent { Event = nameof(OrderStateMachine.Pay) });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);

        var response = Assert.IsType<WorkflowResponse>(ok.Value);
        Assert.NotNull(response);
        Assert.Equal(workflow.Id, response.Id);
        Assert.Equal(nameof(OrderStateMachine.Progressing), response.State);
    }
}
