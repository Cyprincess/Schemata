using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class ResourceControllerCreateShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Create_InputIsValidStudent_ReturnJsonResult() {
        var student = new Student {
            Name  = "Charlie",
            Age   = 18,
            Grade = 1,
        };

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();

        var result = await controller.Create(student);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json);
        Assert.Equal(StatusCodes.Status201Created, json.StatusCode);

        var response = Assert.IsType<Student>(json.Value);
        Assert.NotNull(response);
        Assert.Equal(student.Id, response.Id);
        Assert.Equal(student.Name, response.Name);
        Assert.Equal(student.Timestamp, response.Timestamp);

        var entity = _fixture.Students.LastOrDefault();
        Assert.NotNull(entity);
        Assert.Equal(student.Id, entity.Id);
        Assert.Equal(student.Name, entity.Name);
        Assert.Equal(student.Timestamp, entity.Timestamp);
    }
}
