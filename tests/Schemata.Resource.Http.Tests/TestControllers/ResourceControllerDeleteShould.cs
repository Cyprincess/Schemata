using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class ResourceControllerDeleteShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Delete_InputIsExistingId_ReturnNoContentResult() {
        var student = _fixture.Students.First();

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();

        var result = await controller.Delete(student.Id);

        var empty = Assert.IsType<NoContentResult>(result);
        Assert.NotNull(empty);
        Assert.Equal(StatusCodes.Status204NoContent, empty.StatusCode);
        Assert.Single(_fixture.Students);
    }
}
