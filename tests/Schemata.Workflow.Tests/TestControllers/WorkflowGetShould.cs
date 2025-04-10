using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Workflow.Skeleton.Models;
using Xunit;

namespace Schemata.Workflow.Tests.TestControllers;

public class WorkflowGetShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Get_WithExistingId_ReturnsOkObjectResult() {
        var instance = _fixture.Orders.First();
        var workflow = _fixture.Workflows.First();

        var (controller, _) = _fixture.CreateWorkflowController();

        var result = await controller.Get(workflow.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);

        var response = Assert.IsType<WorkflowResponse>(ok.Value);
        Assert.NotNull(response);
        Assert.Equal(workflow.Id, response.Id);
        Assert.Equal(instance.State, response.State);
    }
}
