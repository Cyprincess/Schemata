using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;

namespace Schemata.Report.Integration.Tests.Fixtures;

/// <summary>
///     Registers the production resource type resolver over the resource types a report host
///     references from its repository entities.
/// </summary>
internal static class ReportTestResources
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
