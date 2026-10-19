using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>
///     Registers the production resource type resolver over the entity types whose
///     <c>[ResourceReference]</c> properties a host stores without the resource pipeline.
/// </summary>
internal static class PushTestResources
{
    internal static void AddResourceTypeResolver(IServiceCollection services, params Type[] resourceTypes) {
        var registry = new ResourceRegistry();
        registry.Add(new(typeof(TestOwner)), []);
        registry.Add(new(typeof(TestUser)), []);
        foreach (var resourceType in resourceTypes) {
            registry.Add(new(resourceType), []);
        }

        services.TryAddSingleton(registry);
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
    }
}
