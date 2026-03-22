using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Tests.Unit;

public class ResourceControllerFeatureProviderShould
{
    private static ControllerFeature Populate(ResourceControllerFeatureProvider provider) {
        var feature = new ControllerFeature();
        provider.PopulateFeature([], feature);
        return feature;
    }

    [Fact]
    public void HttpAnnotatedEntity_ControllerRegistered() {
        var resource = new ResourceAttribute(typeof(Student)) { Endpoints = [HttpResourceAttribute.Name] };
        var provider = new ResourceControllerFeatureProvider {
            Resources = { [resource.Entity.TypeHandle] = resource },
        };
        var feature = Populate(provider);

        Assert.Single(feature.Controllers);
        Assert.Equal(typeof(ResourceController<,,,>), feature.Controllers[0].AsType().GetGenericTypeDefinition());
    }

    [Fact]
    public void NonHttpAnnotatedEntity_NoControllerRegistered() {
        var resource = new ResourceAttribute(typeof(Student)) { Endpoints = ["GRPC"] };
        var provider = new ResourceControllerFeatureProvider {
            Resources = { [resource.Entity.TypeHandle] = resource },
        };
        var feature = Populate(provider);
        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void HttpAnnotatedEntity_CorrectGenericTypeArguments() {
        var resource = new ResourceAttribute(typeof(Student), typeof(Student), typeof(Student), typeof(Student)) {
            Endpoints = [HttpResourceAttribute.Name],
        };
        var provider = new ResourceControllerFeatureProvider {
            Resources = { [resource.Entity.TypeHandle] = resource },
        };
        var feature = Populate(provider);
        var args    = feature.Controllers[0].AsType().GetGenericArguments();
        Assert.Equal(typeof(Student), args[0]);
        Assert.Equal(typeof(Student), args[1]);
        Assert.Equal(typeof(Student), args[2]);
        Assert.Equal(typeof(Student), args[3]);
    }
}
