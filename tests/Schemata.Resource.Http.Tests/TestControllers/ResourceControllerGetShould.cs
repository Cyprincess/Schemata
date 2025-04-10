using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class ResourceControllerGetShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Get_InputIsExistingId_ReturnJsonResult_WithFreshnessTag() {
        var student = _fixture.Students.First();

        var (controller, body) = _fixture.CreateResourceController<Student, Student, Student, Student>();

        var result = await controller.Get(student.Id);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json);
        Assert.Null(json.StatusCode);

        var response = Assert.IsType<Student>(json.Value);
        Assert.NotNull(response);
        Assert.Equal(student.Id, response.Id);
        Assert.Equal(student.Name, response.Name);
        Assert.Equal(student.Timestamp, response.Timestamp);

        var action = new ActionContext {
            HttpContext = controller.HttpContext,
        };
        await json.ExecuteResultAsync(action);

        var raw = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("\"etag\":", raw);
    }
}
