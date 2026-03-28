using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

public class ResourceControllerConventionSchemeShould
{
    [Fact]
    public void Apply_WithScheme_AddsAuthorizeFilter() {
        var convention = new ResourceControllerConvention("TestScheme");
        var model = new ControllerModel(typeof(ResourceController<Student, Student, Student, Student>).GetTypeInfo(), []);

        convention.Apply(model);

        Assert.Contains(model.Filters, f => f is AuthorizeFilter);
    }

    [Fact]
    public void Apply_WithoutScheme_NoAuthorizeFilter() {
        var convention = new ResourceControllerConvention();
        var model = new ControllerModel(typeof(ResourceController<Student, Student, Student, Student>).GetTypeInfo(), []);

        convention.Apply(model);

        Assert.DoesNotContain(model.Filters, f => f is AuthorizeFilter);
    }
}
