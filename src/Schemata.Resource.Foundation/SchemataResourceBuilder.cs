using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Features;

namespace Schemata.Resource.Foundation;

/// <summary>
/// Fluent builder for configuring resource services including authorization, validation, freshness, and resource registration.
/// </summary>
public sealed class SchemataResourceBuilder
{
    /// <summary>
    /// Initializes a new instance with the Schemata options and service collection.
    /// </summary>
    /// <param name="schemata">The Schemata framework options.</param>
    /// <param name="services">The DI service collection.</param>
    public SchemataResourceBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    private SchemataOptions Schemata { get; }

    private IServiceCollection Services { get; }

    /// <summary>
    /// Adds a framework feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The feature type to add.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature(typeof(T));
    }

    /// <summary>
    /// Registers the built-in authorization advisors for all CRUD operations.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithAuthorization() {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(AdviceListRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<>), typeof(AdviceGetRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<>), typeof(AdviceDeleteRequestAuthorize<>)));

        return this;
    }

    /// <summary>
    /// Globally suppresses create-request validation for all resources.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutCreateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressCreateValidation = true);
        return this;
    }

    /// <summary>
    /// Globally suppresses update-request validation for all resources.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutUpdateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressUpdateValidation = true);
        return this;
    }

    /// <summary>
    /// Globally suppresses freshness (ETag) checks and generation for all resources.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutFreshness() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressFreshness = true);
        return this;
    }

    /// <summary>
    /// Registers a resource with explicit entity, request, detail, and summary types.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type.</typeparam>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type.</typeparam>
    /// <typeparam name="TSummary">The summary DTO type.</typeparam>
    /// <param name="endpoints">Optional endpoint names to restrict registration (e.g. HTTP only, gRPC only).</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(IList<string>? endpoints = null)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        var entity  = typeof(TEntity);
        var request = typeof(TRequest);
        var detail  = typeof(TDetail);
        var summary = typeof(TSummary);

        var resource = entity.GetCustomAttribute<ResourceAttribute>() ?? new(entity, request, detail, summary);

        if (endpoints is null) {
            resource.Endpoints = endpoints;
        }

        SchemataResourceFeature.RegisterResource(Services, resource);

        return this;
    }
}
