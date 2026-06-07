using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;
using Xunit;

namespace Schemata.Resource.Tests.Http;

public class ResourceMethodControllerFeatureProviderShould
{
    [Fact]
    public void AddNoControllers_WhenMethodsAreEmpty() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() { [typeof(EntityA).TypeHandle] = new ResourceAttribute(typeof(EntityA)) },
            Methods   = [],
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeClosedController_PerHttpMethod() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new ResourceAttribute(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
            },
            Methods = new() {
                [typeof(EntityA).TypeHandle] = [
                    new ResourceMethodAttribute("run", typeof(HandlerA)),
                ],
            },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        var controller = Assert.Single(feature.Controllers);
        Assert.True(controller.IsGenericType);
        Assert.Equal(typeof(ResourceMethodController<,,,>), controller.GetGenericTypeDefinition());

        var args = controller.GetGenericArguments();
        Assert.Equal(typeof(EntityA),  args[0]);
        Assert.Equal(typeof(RequestA), args[1]);
        Assert.Equal(typeof(ResponseA), args[2]);
        Assert.Equal(typeof(HandlerA),  args[3]);
    }

    [Fact]
    public void SkipResources_WhenEndpointsExcludeHttp() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new ResourceAttribute(typeof(EntityA)) { Endpoints = [GrpcResourceAttribute.Name] },
            },
            Methods = new() {
                [typeof(EntityA).TypeHandle] = [
                    new ResourceMethodAttribute("run", typeof(HandlerA)),
                ],
            },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeMultipleControllers_ForMultipleMethodsOnSameEntity() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new ResourceAttribute(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
            },
            Methods = new() {
                [typeof(EntityA).TypeHandle] = [
                    new ResourceMethodAttribute("run",     typeof(HandlerA)),
                    new ResourceMethodAttribute("archive", typeof(HandlerB)),
                ],
            },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Equal(2, feature.Controllers.Count);
        Assert.Contains(feature.Controllers, c => c.GetGenericArguments()[3] == typeof(HandlerA));
        Assert.Contains(feature.Controllers, c => c.GetGenericArguments()[3] == typeof(HandlerB));
    }

    [Fact]
    public void SkipMethods_WhenResourceIsNotRegistered() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = [],
            Methods   = new() {
                [typeof(EntityA).TypeHandle] = [
                    new ResourceMethodAttribute("run", typeof(HandlerA)),
                ],
            },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    public sealed class EntityA : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class RequestA : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class ResponseA : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class HandlerA : IResourceMethodHandler<EntityA, RequestA, ResponseA>
    {
        public ValueTask<ResponseA> InvokeAsync(
            string?           name,
            RequestA          request,
            EntityA           entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new ResponseA());
    }

    public sealed class HandlerB : IResourceMethodHandler<EntityA, RequestA, ResponseA>
    {
        public ValueTask<ResponseA> InvokeAsync(
            string?           name,
            RequestA          request,
            EntityA           entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new ResponseA());
    }
}
