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
            Resources = new() { [typeof(EntityA).TypeHandle] = new(typeof(EntityA)) }, Methods = [],
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeClosedController_PerHttpMethod() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
            },
            Methods = new() { [typeof(EntityA).TypeHandle] = [new("run", typeof(HandlerA))] },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        var controller = Assert.Single(feature.Controllers);
        Assert.True(controller.IsGenericType);
        Assert.Equal(typeof(ResourceMethodController<,,,>), controller.GetGenericTypeDefinition());

        var args = controller.GetGenericArguments();
        Assert.Equal(typeof(EntityA), args[0]);
        Assert.Equal(typeof(RequestA), args[1]);
        Assert.Equal(typeof(ResponseA), args[2]);
        Assert.Equal(typeof(HandlerA), args[3]);
    }

    [Fact]
    public void SkipResources_WhenEndpointsExcludeHttp() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new(typeof(EntityA)) { Endpoints = [GrpcResourceAttribute.Name] },
            },
            Methods = new() { [typeof(EntityA).TypeHandle] = [new("run", typeof(HandlerA))] },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeMultipleControllers_ForMultipleMethodsOnSameEntity() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Resources = new() {
                [typeof(EntityA).TypeHandle] = new(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
            },
            Methods = new() {
                [typeof(EntityA).TypeHandle] = [new("run", typeof(HandlerA)), new("archive", typeof(HandlerB))],
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
            Resources = [], Methods = new() { [typeof(EntityA).TypeHandle] = [new("run", typeof(HandlerA))] },
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    #region Nested type: EntityA

    public sealed class EntityA : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: HandlerA

    public sealed class HandlerA : IResourceMethodHandler<EntityA, RequestA, ResponseA>
    {
        #region IResourceMethodHandler<EntityA,RequestA,ResponseA> Members

        public ValueTask<ResponseA> InvokeAsync(
            string?           name,
            RequestA          request,
            EntityA?          entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) {
            return ValueTask.FromResult(new ResponseA());
        }

        #endregion
    }

    #endregion

    #region Nested type: HandlerB

    public sealed class HandlerB : IResourceMethodHandler<EntityA, RequestA, ResponseA>
    {
        #region IResourceMethodHandler<EntityA,RequestA,ResponseA> Members

        public ValueTask<ResponseA> InvokeAsync(
            string?           name,
            RequestA          request,
            EntityA?          entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) {
            return ValueTask.FromResult(new ResponseA());
        }

        #endregion
    }

    #endregion

    #region Nested type: RequestA

    public sealed class RequestA : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion

    #region Nested type: ResponseA

    public sealed class ResponseA : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
