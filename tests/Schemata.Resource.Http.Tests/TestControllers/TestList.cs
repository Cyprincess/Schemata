using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class TestList
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task List() {
        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await controller.List(new());

        var json     = Assert.IsType<JsonResult>(result);
        var response = Assert.IsAssignableFrom<ListResponse<Student>>(json.Value);
        Assert.Equal(response.TotalSize, response.Entities?.Count());
        Assert.Equal(_fixture.Students.Count, response.TotalSize);
        Assert.Null(response.NextPageToken);
    }

    [Fact]
    public async Task ListEntitiesProperty() {
        var (controller, body) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await controller.List(new());

        var json = Assert.IsType<JsonResult>(result);
        var action = new ActionContext {
            HttpContext = controller.HttpContext,
        };
        await json.ExecuteResultAsync(action);

        var raw = Encoding.UTF8.GetString(body.ToArray());

        Assert.Contains("\"students\":", raw);
    }
}
