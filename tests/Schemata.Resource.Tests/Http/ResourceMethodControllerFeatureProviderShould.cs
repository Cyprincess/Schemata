using Schemata.Core.Building;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Http;
using Xunit;

namespace Schemata.Resource.Tests.Http;

public class ResourceMethodControllerFeatureProviderShould
{
    [Fact]
    public void AddNoControllers_WhenMethodsAreEmpty() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Registry = Registry(new(typeof(EntityA))),
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeClosedController_PerHttpMethod() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Registry = Registry(
                new(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
                new ResourceMethodAttribute("run", typeof(HandlerA))),
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        var controller = Assert.Single(feature.Controllers);
        Assert.True(controller.IsGenericType);
        Assert.Equal(typeof(ResourceMethodController<,,>), controller.GetGenericTypeDefinition());

        var args = controller.GetGenericArguments();
        Assert.Equal(typeof(EntityA), args[0]);
        Assert.Equal(typeof(RequestA), args[1]);
        Assert.Equal(typeof(ResponseA), args[2]);
    }

    [Fact]
    public void SkipResources_WhenEndpointsExcludeHttp() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Registry = Registry(
                new(typeof(EntityA)) { Endpoints = [GrpcResourceAttribute.Name] },
                new ResourceMethodAttribute("run", typeof(HandlerA))),
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    [Fact]
    public void SynthesizeMultipleControllers_ForMultipleMethodsOnSameEntity() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Registry = Registry(
                new(typeof(EntityA)) { Endpoints = [HttpResourceAttribute.Name] },
                new ResourceMethodAttribute("run", typeof(HandlerA)),
                new ResourceMethodAttribute("archive", typeof(HandlerB))),
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Equal(2, feature.Controllers.Count);
        Assert.Contains(feature.Controllers, controller => controller.GetGenericArguments()[1] == typeof(RequestA));
        Assert.Contains(feature.Controllers, controller => controller.GetGenericArguments()[1] == typeof(RequestB));
    }

    [Fact]
    public void SkipMethods_WhenResourceIsNotRegistered() {
        var provider = new ResourceMethodControllerFeatureProvider {
            Registry = new ResourceRegistry(),
        };
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        Assert.Empty(feature.Controllers);
    }

    private static ResourceRegistry Registry(
        ResourceAttribute                 resource,
        params ResourceMethodAttribute[] methods
    ) {
        var registry = new ResourceRegistry();
        registry.Add(resource, methods);
        return registry;
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

    public sealed class HandlerA : IRequestHandler<RequestA, ResponseA>
    {
        public Task<ResponseA> HandleAsync(
            RequestA        request,
            CancellationToken ct = default
        ) {
            return Task.FromResult(new ResponseA());
        }
    }

    #endregion

    #region Nested type: HandlerB

    public sealed class HandlerB : IRequestHandler<RequestB, ResponseA>
    {
        public Task<ResponseA> HandleAsync(
            RequestB        request,
            CancellationToken ct = default
        ) {
            return Task.FromResult(new ResponseA());
        }
    }

    #endregion

    #region Nested type: RequestA

    public sealed class RequestA : IRequest<ResponseA>, IRequestPrincipal, ICanonicalName
    {
        public string?           Name          { get; set; }
        public string?           CanonicalName { get; set; }
        public ClaimsPrincipal? Principal { get; set; }
    }

    #endregion

    #region Nested type: RequestB

    public sealed class RequestB : IRequest<ResponseA>, IRequestPrincipal, ICanonicalName
    {
        public string?          Name          { get; set; }
        public string?          CanonicalName { get; set; }
        public ClaimsPrincipal? Principal     { get; set; }
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
