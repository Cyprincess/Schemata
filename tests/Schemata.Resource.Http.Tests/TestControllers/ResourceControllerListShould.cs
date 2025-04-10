using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class ResourceControllerListShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task List_InputIsEmpty_ReturnJsonResult_WithEntitiesProperty() {
        var (controller, body) = _fixture.CreateResourceController<Student, Student, Student, Student>();

        var result = await controller.List(new());

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json);
        Assert.Null(json.StatusCode);

        var response = Assert.IsType<ListResponse<Student>>(json.Value);
        Assert.NotNull(response);
        Assert.NotNull(response.Entities);
        Assert.Equal(_fixture.Students.Count, response.Entities.Count());
        Assert.Equal(_fixture.Students.Count, response.TotalSize);
        Assert.Null(response.NextPageToken);

        var action = new ActionContext {
            HttpContext = controller.HttpContext,
        };
        await json.ExecuteResultAsync(action);

        var raw = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("\"students\":", raw);
    }
}
