using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class TestUpdate
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Update() {
        var student = _fixture.Students.First();

        var request = new Student {
            Id    = 1,
            Name  = "Alice",
            Age   = 18,
            Grade = 2,
        };

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await controller.Update(student.Id, request);

        var json     = Assert.IsType<JsonResult>(result);
        var response = Assert.IsAssignableFrom<Student>(json.Value);
        Assert.Equal(student.Name, response.Name);
        Assert.Equal(request.Grade, response.Grade);
    }
}
