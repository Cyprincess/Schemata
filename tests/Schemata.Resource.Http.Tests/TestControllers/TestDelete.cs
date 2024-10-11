using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Schemata.Resource.Http.Tests.TestControllers;

public class TestDelete
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Delete() {
        var student = _fixture.Students.First();

        var (controller, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await controller.Delete(student.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(_fixture.Students);
    }
}
