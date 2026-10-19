using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

/// <summary>
///     Registers the production resource type resolver over the entity types a host stores in
///     <c>[ResourceReference]</c> properties, for hosts that build their repositories without the
///     resource pipeline.
/// </summary>
internal static class TestResourceTypeResolver
{
    internal static void AddResourceTypeResolver(IServiceCollection services, params Type[] resourceTypes) {
        var registry = new ResourceRegistry();
        foreach (var resourceType in resourceTypes) {
            registry.Add(new(resourceType), []);
        }

        services.TryAddSingleton(registry);
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
    }
}
