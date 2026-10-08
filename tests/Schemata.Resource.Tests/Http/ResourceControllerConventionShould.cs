using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Http;
using Xunit;

namespace Schemata.Resource.Tests.Http;

/// <summary>
///     Behavioral coverage for issue #34: operation identity for the whitelist and the anonymous
///     projection derives from the registered standard <see cref="MethodInfo" />, never from the
///     display <see cref="ActionModel.ActionName" />. MVC discovery strips the Async suffix from
///     display names by default, so these models are built the way the default application model
///     provider builds them — real <see cref="MethodInfo" /> with discovery-stripped action
///     names — instead of naming actions after CLR methods.
/// </summary>
public class ResourceControllerConventionShould
{

    [Fact]
    public void Drop_StandardActions_OutsideTheRegisteredOperationWhitelist() {
        var registry = new ResourceRegistry();
        registry.Add(new(typeof(SecuredItem)) {
            Operations = [Operations.List, Operations.Get],
        }, []);

        var controller = ConventionController<SecuredItem>(registry, scheme: null);

        // Discovery strips Async from display names; the whitelist still resolves and drops
        // Create/Update/Delete while keeping List/Get.
        Assert.Null(controller.Actions.FirstOrDefault(a => a.ActionName == nameof(Operations.Create)));
        Assert.Null(controller.Actions.FirstOrDefault(a => a.ActionName == nameof(Operations.Update)));
        Assert.Null(controller.Actions.FirstOrDefault(a => a.ActionName == nameof(Operations.Delete)));
        Assert.NotNull(controller.Actions.FirstOrDefault(a => a.ActionName == nameof(Operations.List)));
        Assert.NotNull(controller.Actions.FirstOrDefault(a => a.ActionName == nameof(Operations.Get)));
    }


    private static ControllerModel ConventionController<TEntity>(ResourceRegistry registry, string? scheme)
        where TEntity : class, ICanonicalName {
        var controllerType = typeof(ResourceController<TEntity, TEntity, TEntity, TEntity>).GetTypeInfo();
        var model = new ControllerModel(controllerType, []) {
            ControllerName = "PlaceholderName",
        };
        model.Selectors.Add(new());

        // Build every standard action the way default MVC discovery does: the real closed
        // MethodInfo with its display name stripped of the Async suffix.
        foreach (var name in StandardMethodNames) {
            var method = controllerType.GetMethods()
                                       .First(m => m.Name == name && m.GetParameters().Length == ExpectedParameters(name));
            var action = new ActionModel(method, []) {
                Controller = model,
                ActionName = name[..^"Async".Length],
            };
            action.Selectors.Add(new());
            model.Actions.Add(action);
        }

        new ResourceControllerConvention(registry, scheme).Apply(model);
        return model;
    }


    private static readonly string[] StandardMethodNames =
        [nameof(ResourceController<,,,>.ListAsync), nameof(ResourceController<,,,>.GetAsync),
         nameof(ResourceController<,,,>.CreateAsync), nameof(ResourceController<,,,>.UpdateAsync),
         nameof(ResourceController<,,,>.DeleteAsync)];

    private static int ExpectedParameters(string name) {
        return name switch {
            nameof(ResourceController<,,,>.ListAsync)   => 1,
            nameof(ResourceController<,,,>.GetAsync)    => 1,
            nameof(ResourceController<,,,>.CreateAsync) => 1,
            nameof(ResourceController<,,,>.UpdateAsync) => 2,
            nameof(ResourceController<,,,>.DeleteAsync) => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    #region Nested type: SecuredItem

    [CanonicalName("securedItems/{secured_item}")]
    [Anonymous(nameof(Operations.Get), "announce")]
    public sealed class SecuredItem : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
