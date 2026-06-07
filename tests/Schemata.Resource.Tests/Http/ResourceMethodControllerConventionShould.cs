using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;
using Xunit;

namespace Schemata.Resource.Tests.Http;

public class ResourceMethodControllerConventionShould
{
    [Fact]
    public void DoNothing_ForUnrelatedControllerType() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new ResourceMethodAttribute("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var model = BuildController(typeof(UnrelatedController).GetTypeInfo());
        var initialTemplate = model.Selectors[0].AttributeRouteModel!.Template;

        convention.Apply(model);

        Assert.Equal(initialTemplate, model.Selectors[0].AttributeRouteModel!.Template);
        Assert.Equal("Untouched", model.Actions[0].ActionName);
    }

    [Fact]
    public void RewriteInstanceScopeAction_To_NameColonVerb() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new ResourceMethodAttribute("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        var actionTemplate = model.Actions[0].Selectors[0].AttributeRouteModel!.Template;
        Assert.Equal("{name}:run", actionTemplate);
        Assert.Equal("Invoke_run", model.Actions[0].ActionName);
    }

    [Fact]
    public void RewriteCollectionScopeAction_To_ColonVerb() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [
                new ResourceMethodAttribute("batchCreate", typeof(HandlerB), ResourceMethodScope.Collection),
            ],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        var actionTemplate = model.Actions[0].Selectors[0].AttributeRouteModel!.Template;
        Assert.Equal(":batchCreate", actionTemplate);
        Assert.Equal("Invoke_batchCreate", model.Actions[0].ActionName);
    }

    [Fact]
    public void RewriteControllerRoute_ToCollectionPath() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new ResourceMethodAttribute("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        var controllerTemplate = model.Selectors[0].AttributeRouteModel!.Template;
        Assert.Equal("~/v1/entityBs", controllerTemplate);
        Assert.Equal("EntityBs", model.ControllerName);
    }

    [Fact]
    public void SkipWhenMethodNotRegistered_ForHandlerType() {
        var convention = new ResourceMethodControllerConvention([]);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);
        var initialTemplate = model.Selectors[0].AttributeRouteModel!.Template;
        var initialAction   = model.Actions[0].ActionName;

        convention.Apply(model);

        Assert.Equal(initialTemplate, model.Selectors[0].AttributeRouteModel!.Template);
        Assert.Equal(initialAction,   model.Actions[0].ActionName);
    }

    private static ControllerModel BuildController(TypeInfo controllerType) {
        var model = new ControllerModel(controllerType, []) {
            ControllerName = "PlaceholderName",
        };
        model.Selectors.Add(new SelectorModel {
            AttributeRouteModel = new AttributeRouteModel { Template = "~/Resource" },
        });

        var actionMethod = controllerType.GetMethods()
                                         .First(m => m.Name == "InvokeAsync" || m.Name == "Untouched");
        var action = new ActionModel(actionMethod, []) {
            Controller = model,
            ActionName = controllerType == typeof(UnrelatedController) ? "Untouched" : "InvokeAsync",
        };
        action.Selectors.Add(new SelectorModel {
            AttributeRouteModel = new AttributeRouteModel(),
        });
        model.Actions.Add(action);

        return model;
    }

    [CanonicalName("entityBs/{entity_b}")]
    public sealed class EntityB : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class RequestB : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class ResponseB : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class HandlerB : IResourceMethodHandler<EntityB, RequestB, ResponseB>
    {
        public ValueTask<ResponseB> InvokeAsync(
            string?           name,
            RequestB          request,
            EntityB           entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new ResponseB());
    }

    public sealed class UnrelatedController
    {
        public void Untouched() { }
    }
}
