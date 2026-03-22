using System;
using System.Threading.Tasks;
using Schemata.Resource.Http.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Http.Tests.Unit;

public class ResourceControllerETagShould
{
    [Fact]
    public async Task Update_BodyETagSet_NotOverriddenByQueryOrHeader() {
        var fixture = new TestFixture();
        var (ctrl, context)                 = fixture.CreateResourceController<Student, Student, Student, Student>();
        context.Request.Headers["If-Match"] = "W/\"header-tag\"";
        context.Request.QueryString         = new($"?{Parameters.EntityTag}={Uri.EscapeDataString("W/\"query-tag\"")}");

        var request = new Student { FullName = "Updated", EntityTag = "W/\"body-tag\"" };

        try {
            await ctrl.UpdateAsync(fixture.Students[0].Name!, request);
        } catch { }

        Assert.Equal("W/\"body-tag\"", request.EntityTag);
    }

    [Fact]
    public async Task Update_NoBodyETag_UsesQueryParam() {
        var fixture = new TestFixture();
        var (ctrl, context)                 = fixture.CreateResourceController<Student, Student, Student, Student>();
        context.Request.QueryString         = new($"?{Parameters.EntityTag}={Uri.EscapeDataString("W/\"query-tag\"")}");
        context.Request.Headers["If-Match"] = "W/\"header-tag\"";

        var request = new Student { FullName = "Updated" };

        try {
            await ctrl.UpdateAsync(fixture.Students[0].Name!, request);
        } catch { }

        Assert.Equal("W/\"query-tag\"", request.EntityTag);
    }

    [Fact]
    public async Task Update_NoBodyOrQuery_UsesIfMatchHeader() {
        var fixture = new TestFixture();
        var (ctrl, context)                 = fixture.CreateResourceController<Student, Student, Student, Student>();
        context.Request.Headers["If-Match"] = "W/\"header-tag\"";

        var request = new Student { FullName = "Updated" };

        try {
            await ctrl.UpdateAsync(fixture.Students[0].Name!, request);
        } catch { }

        Assert.Equal("W/\"header-tag\"", request.EntityTag);
    }
}
