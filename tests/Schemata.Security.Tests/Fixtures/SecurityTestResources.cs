using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;

namespace Schemata.Security.Tests.Fixtures;

/// <summary>
///     Registers the production resource type resolver over the resource types referenced by
///     security rows in hosts that build their repositories without the resource pipeline.
/// </summary>
internal static class SecurityTestResources
{
    internal static void AddResourceTypeResolver(IServiceCollection services) {
        var registry = new ResourceRegistry();
        registry.Add(new(typeof(TestUser)), []);
        registry.Add(new(typeof(TestApplication)), []);
        registry.Add(new(typeof(TestAuthorization)), []);
        services.TryAddSingleton(registry);
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
    }
}

[CanonicalName("users/{user}")]
public sealed class TestUser : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}

[CanonicalName("applications/{application}")]
public sealed class TestApplication : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}

[CanonicalName("authorizations/{authorization}")]
public sealed class TestAuthorization : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
