using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Expressions.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Features;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Fluent builder for configuring the resource system: authorization, validation suppression,
///     freshness suppression, and per-resource registration.
/// </summary>
public sealed class SchemataResourceBuilder : IExpressionLanguageBuilder
{
    /// <summary>
    ///     Initializes a new instance with the Schemata options and service collection.
    /// </summary>
    /// <param name="schemata">The <see cref="SchemataOptions" />.</param>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    public SchemataResourceBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;

        // Bind only when this builder enables a language so a builder that registers resources
        // without filtering (e.g. a feature's internal resource) does not clear another's profile.
        Services.Configure<SchemataResourceOptions>(o => {
            if (Languages.Languages.Count > 0) {
                o.Expressions = Languages;
            }
        });
    }

    private SchemataOptions Schemata { get; }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public ExpressionLanguageProfile Languages { get; } = new();

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }

    /// <summary>
    ///     Registers the built-in authorization and anonymous-access advisors for all CRUD operations.
    /// </summary>
    /// <param name="scheme">
    ///     An optional ASP.NET Core authentication scheme name. When set, resource endpoints
    ///     authenticate using this scheme before advisors run.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithAuthorization(string? scheme = null) {
        if (!string.IsNullOrWhiteSpace(scheme)) {
            Services.Configure<SchemataResourceOptions>(o => o.AuthenticationScheme = scheme);
        }

        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(AdviceListRequestAnonymous<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<>), typeof(AdviceGetRequestAnonymous<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestAnonymous<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestAnonymous<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<>), typeof(AdviceDeleteRequestAnonymous<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>), typeof(AdviceMethodRequestAnonymous<,>)));

        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(AdviceListRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<>), typeof(AdviceGetRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<>), typeof(AdviceDeleteRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>), typeof(AdviceMethodRequestAuthorize<,>)));

        return this;
    }

    /// <summary>
    ///     Globally suppresses create-request validation
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutCreateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressCreateValidation = true);
        return this;
    }

    /// <summary>
    ///     Globally suppresses update-request validation
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutUpdateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressUpdateValidation = true);
        return this;
    }

    /// <summary>
    ///     Globally suppresses freshness (ETag) checks and generation
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutFreshness() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressFreshness = true);
        return this;
    }

    /// <summary>
    ///     Registers a resource with explicit entity, request, detail, and summary types
    ///     per <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type.</typeparam>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type.</typeparam>
    /// <typeparam name="TSummary">The summary DTO type.</typeparam>
    /// <param name="endpoints">Optional endpoint names to restrict registration.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(IList<string>? endpoints = null)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        return Use<TEntity, TRequest, TDetail, TSummary>(endpoints, null);
    }

    /// <summary>
    ///     Registers a resource, restricting it to the transports selected through
    ///     <paramref name="transports" />. A <see langword="null" /> or empty selector exposes the
    ///     resource on every registered endpoint, matching the no-argument overload.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type.</typeparam>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type.</typeparam>
    /// <typeparam name="TSummary">The summary DTO type.</typeparam>
    /// <param name="transports">A callback selecting the transports that expose this resource.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(Action<ResourceEndpointSelector>? transports)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        var selector = new ResourceEndpointSelector();
        transports?.Invoke(selector);

        var endpoints = selector.Endpoints.Count > 0 ? new List<string>(selector.Endpoints) : null;

        return Use<TEntity, TRequest, TDetail, TSummary>(endpoints, null);
    }

    /// <summary>
    ///     Registers a resource with explicit type roles and allows callers to configure
    ///     operations, endpoints, and custom methods programmatically.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type.</typeparam>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type.</typeparam>
    /// <typeparam name="TSummary">The summary DTO type.</typeparam>
    /// <param name="endpoints">Optional endpoint names to restrict registration.</param>
    /// <param name="configure">Optional resource metadata callback.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        IList<string>?             endpoints,
        Action<ResourceAttribute>? configure
    )
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
            resource.Endpoints = null;
        } else if (resource.Endpoints is null) {
            resource.Endpoints = endpoints;
        } else {
            foreach (var endpoint in endpoints) {
                resource.Endpoints.Add(endpoint);
            }
        }

        configure?.Invoke(resource);

        SchemataResourceFeature.RegisterResource(Services, resource);

        return this;
    }
}
