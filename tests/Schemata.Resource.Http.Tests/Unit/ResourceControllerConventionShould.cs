using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Tests.Unit;

public class ResourceControllerConventionShould
{
    private static ControllerFeature Populate(ResourceControllerFeatureProvider provider) {
        var feature = new ControllerFeature();
        provider.PopulateFeature([], feature);
        return feature;
    }

    [Fact]
    public void HttpAnnotatedEntity_GeneratesControllerWithCorrectGenericArgs() {
        var resource = new ResourceAttribute(typeof(Student)) { Endpoints = [HttpResourceAttribute.Name] };
        var provider = new ResourceControllerFeatureProvider {
            Resources = { [resource.Entity.TypeHandle] = resource },
        };
        var feature = Populate(provider);

        Assert.Single(feature.Controllers);
        var args = feature.Controllers[0].AsType().GetGenericArguments();
        Assert.Equal(typeof(Student), args[0]);
    }

    [Fact]
    public void NonHttpResource_NoController() {
        var resource = new ResourceAttribute(typeof(Student)) { Endpoints = ["GRPC"] };
        var provider = new ResourceControllerFeatureProvider {
            Resources = { [resource.Entity.TypeHandle] = resource },
        };
        var feature = Populate(provider);
        Assert.Empty(feature.Controllers);
    }
}
