using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class ResourceControllerUpdateShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Update_InputIsValidStudent_ReturnJsonResult() {
        var student = _fixture.Students.First();

        var request = new Student {
            Id    = 1,
            Name  = "Alice",
            Age   = 18,
            Grade = 2,
        };

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();

        var result = await controller.Update(student.Id, request);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json);
        Assert.Null(json.StatusCode);

        var response = Assert.IsType<Student>(json.Value);
        Assert.NotNull(response);
        Assert.Equal(student.Name, response.Name);
        Assert.Equal(request.Grade, response.Grade);
    }
}
