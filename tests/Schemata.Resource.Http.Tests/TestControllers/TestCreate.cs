using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class TestCreate
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Create() {
        var student = new Student {
            Name  = "Charlie",
            Age   = 18,
            Grade = 1,
        };

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await controller.Create(student);

        var json     = Assert.IsType<JsonResult>(result);
        var response = Assert.IsAssignableFrom<Student>(json.Value);
        Assert.Equal(student.Id, response.Id);
        Assert.Equal(student.Name, response.Name);
        Assert.Equal(student.Timestamp, response.Timestamp);

        var entity = _fixture.Students.LastOrDefault();
        Assert.Equal(student.Id, entity?.Id);
        Assert.Equal(student.Name, entity?.Name);
        Assert.Equal(student.Timestamp, entity?.Timestamp);
    }
}
