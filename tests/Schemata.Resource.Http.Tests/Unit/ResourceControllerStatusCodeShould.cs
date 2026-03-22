using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Resource.Http.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Tests.Unit;

public class ResourceControllerStatusCodeShould
{
    private readonly TestFixture _fixture = new();

    [Fact]
    public async Task Create_ReturnsStatus201() {
        var (ctrl, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await ctrl.CreateAsync(new() { FullName = "Test" });
        var json   = Assert.IsType<JsonResult>(result);
        Assert.Equal(StatusCodes.Status201Created, json.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsStatus204() {
        var (ctrl, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var student = _fixture.Students[0];
        var result  = await ctrl.DeleteAsync(student.Name!);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task List_ReturnsNullStatusCode() {
        var (ctrl, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var result = await ctrl.ListAsync(new());
        var json   = Assert.IsType<JsonResult>(result);
        Assert.Null(json.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNullStatusCode() {
        var (ctrl, _) = _fixture.CreateResourceController<Student, Student, Student, Student>();
        var student = _fixture.Students[0];
        var result  = await ctrl.UpdateAsync(student.Name!, new() { Id = student.Id, FullName = student.FullName });
        var json    = Assert.IsType<JsonResult>(result);
        Assert.Null(json.StatusCode);
    }
}
