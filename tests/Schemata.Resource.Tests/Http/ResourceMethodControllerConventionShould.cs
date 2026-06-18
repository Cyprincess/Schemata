using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
            [typeof(EntityB).TypeHandle] = [new("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var model = BuildController(typeof(UnrelatedController).GetTypeInfo());
        var initialTemplate = model.Selectors[0].AttributeRouteModel!.Template;

        convention.Apply(model);

        Assert.Equal(initialTemplate, model.Selectors[0].AttributeRouteModel!.Template);
        Assert.Equal("Untouched", model.Actions[0].ActionName);
    }

    [Fact]
    public void RewriteInstanceScopeAction_To_AbsoluteNameColonVerb() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        var actionTemplate = model.Actions[0].Selectors[0].AttributeRouteModel!.Template;
        Assert.Equal("~/v1/entityBs/{name}:run", actionTemplate);
        Assert.Equal("Invoke_run", model.Actions[0].ActionName);
    }

    [Fact]
    public void CloneActionPerVerb_WhenHandlerRegisteredForMultipleVerbs() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [
                new("archive", typeof(HandlerB)),
                new("restore", typeof(HandlerB)),
            ],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        // The single closed controller fans out into one action per verb instead of collapsing.
        Assert.Equal(2, model.Actions.Count);
        var templates = model.Actions.Select(a => a.Selectors[0].AttributeRouteModel!.Template).ToList();
        Assert.Contains("~/v1/entityBs/{name}:archive", templates);
        Assert.Contains("~/v1/entityBs/{name}:restore", templates);
        Assert.Contains(model.Actions, a => a.ActionName == "Invoke_archive");
        Assert.Contains(model.Actions, a => a.ActionName == "Invoke_restore");
    }

    [Fact]
    public void RewriteCollectionScopeAction_To_AbsoluteColonVerb() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [
                new("batchCreate", typeof(HandlerB), ResourceMethodScope.Collection),
            ],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        var actionTemplate = model.Actions[0].Selectors[0].AttributeRouteModel!.Template;
        Assert.Equal("~/v1/entityBs:batchCreate", actionTemplate);
        Assert.Equal("Invoke_batchCreate", model.Actions[0].ActionName);
    }

    [Fact]
    public void NullControllerRoute_AndSetPluralName() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType);

        convention.Apply(model);

        Assert.Null(model.Selectors[0].AttributeRouteModel);
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

    [Fact]
    public void RebindGetMethod_ToGetConstraintAndQueryBinding() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [
                new("preview", typeof(HandlerB)) { Method = ResourceHttpMethod.Get },
            ],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType, true);
        model.Actions[0].Selectors[0].ActionConstraints.Add(
            new HttpMethodActionConstraint(["POST"]));

        convention.Apply(model);

        var constraint = Assert.IsType<HttpMethodActionConstraint>(
            Assert.Single(model.Actions[0].Selectors[0].ActionConstraints));
        Assert.Equal(["GET"], constraint.HttpMethods);

        var parameter = Assert.Single(model.Actions[0].Parameters, p => p.ParameterName == "request");
        Assert.Equal(
            BindingSource.Query,
            parameter.BindingInfo?.BindingSource);
    }

    [Fact]
    public void KeepPostConstraint_ForDefaultMethod() {
        var methods = new Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> {
            [typeof(EntityB).TypeHandle] = [new("run", typeof(HandlerB))],
        };
        var convention = new ResourceMethodControllerConvention(methods);

        var controllerType = typeof(ResourceMethodController<EntityB, RequestB, ResponseB, HandlerB>).GetTypeInfo();
        var model          = BuildController(controllerType, true);
        model.Actions[0].Selectors[0].ActionConstraints.Add(
            new HttpMethodActionConstraint(["POST"]));

        convention.Apply(model);

        var constraint = Assert.IsType<HttpMethodActionConstraint>(
            Assert.Single(model.Actions[0].Selectors[0].ActionConstraints));
        Assert.Equal(["POST"], constraint.HttpMethods);

        var parameter = Assert.Single(model.Actions[0].Parameters, p => p.ParameterName == "request");
        Assert.Null(parameter.BindingInfo);
    }

    private static ControllerModel BuildController(TypeInfo controllerType, bool withRequestParameter) {
        var model = BuildController(controllerType);

        if (withRequestParameter) {
            var actionMethod = controllerType.GetMethods().First(m => m.Name == "InvokeAsync");
            var info         = actionMethod.GetParameters().First(p => p.Name == "request");
            model.Actions[0].Parameters.Add(new(info, []) {
                ParameterName = "request",
                Action        = model.Actions[0],
            });
        }

        return model;
    }

    private static ControllerModel BuildController(TypeInfo controllerType) {
        var model = new ControllerModel(controllerType, []) {
            ControllerName = "PlaceholderName",
        };
        model.Selectors.Add(new() {
            AttributeRouteModel = new() { Template = "~/Resource" },
        });

        var actionMethod = controllerType.GetMethods()
                                         .First(m => m.Name == "InvokeAsync" || m.Name == "Untouched");
        var action = new ActionModel(actionMethod, []) {
            Controller = model,
            ActionName = controllerType == typeof(UnrelatedController) ? "Untouched" : "InvokeAsync",
        };
        action.Selectors.Add(new() {
            AttributeRouteModel = new(),
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
            EntityB?          entity,
            ClaimsPrincipal?  principal,
            CancellationToken ct
        ) => ValueTask.FromResult(new ResponseB());
    }

    public sealed class UnrelatedController
    {
        public void Untouched() { }
    }
}
