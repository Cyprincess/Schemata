using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Expressions.Skeleton;
using Schemata.Core.Features;
using Schemata.Security.Skeleton;

namespace Schemata.Core.Building;

/// <summary>
///     Fluent builder for configuring the resource system: authorization, validation suppression,
///     freshness suppression, and per-resource registration.
/// </summary>
public sealed class SchemataResourceBuilder : IExpressionLanguageBuilder, IResourceBuilder
{

    /// <summary>
    ///     Initializes a new instance with the Schemata options and service collection.
    /// </summary>
    /// <param name="schemata">The <see cref="SchemataOptions" />.</param>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    public SchemataResourceBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
        Registry = ResourceRegistry.GetOrAdd(schemata, services);
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => Registry.ActivateAuthentication(services),
            services => Registry.ActivateAuthorization(services),
            scheme => {
                if (!string.IsNullOrWhiteSpace(scheme)) {
                    Services.Configure<SchemataResourceOptions>(options => options.AuthenticationScheme = scheme);
                }
            });

        // Bind only when this builder enables a language so a builder that registers resources
        // without filtering (e.g. a feature's internal resource) does not clear another's profile.
        Services.Configure<SchemataResourceOptions>(o => {
            if (Languages.Languages.Count > 0) {
                o.Expressions = Languages;
            }
        });
    }


    public SchemataOptions Schemata { get; }
    private ResourceRegistry Registry { get; }


    /// <summary>
    ///     The authentication scheme stamped onto every resource this builder registers that does
    ///     not already declare one. A component that owns its own builder instance sets this to
    ///     demand its own scheme without touching the resource system's global default.
    /// </summary>
    public string? AuthenticationScheme { get; set; }

    public IServiceCollection Services { get; }

    public ExpressionLanguageProfile Languages { get; } = new();

    public SchemataResourceBuilder WithAuthorization<TEntity>() where TEntity : class, ICanonicalName {
        Registry.ActivateAuthorization(Services, typeof(TEntity));
        return this;
    }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }


    /// <summary>
    ///     Excludes the create-request validation stage from every resource
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
    ///     A <c>ValidateOnly</c> create request still terminates as a dry-run.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutCreateValidation() {
        Registry.WithoutCreateValidation(Services);
        return this;
    }

    /// <summary>
    ///     Excludes the update-request validation stage from every resource
    ///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>.
    ///     A <c>ValidateOnly</c> update request still terminates as a dry-run.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutUpdateValidation() {
        Registry.WithoutUpdateValidation(Services);
        return this;
    }

    /// <summary>
    ///     Excludes the freshness (ETag) check and response stages from every resource
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder WithoutFreshness() {
        Registry.WithoutFreshness(Services);
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
    ///     <paramref name="transports" />. A <see langword="null" /> or empty selector uses the
    ///     entity's endpoint declarations, or every registered endpoint when none are declared.
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

        var declared = entity.GetCustomAttribute<ResourceAttribute>();
        var resource = new ResourceAttribute(entity, request, detail, summary) {
            Endpoints = declared?.Endpoints,
            Operations = declared?.Operations,
            Methods = declared?.Methods,
            AuthenticationScheme = declared?.AuthenticationScheme,
            DefaultPageSize = declared?.DefaultPageSize ?? 0,
            MaxPageSize = declared?.MaxPageSize ?? 0,
        };
        if (declared?.ConfiguredTotalSize is { } totalSize) resource.TotalSize = totalSize;

        return Register(resource, endpoints, configure);
    }

    /// <summary>
    ///     Registers a resource that declares its own type roles through <see cref="ResourceAttribute" />,
    ///     so the DTO types need not be repeated as type arguments. Resources are never discovered
    ///     automatically: a <c>[Resource]</c>-decorated entity is registered only by this call or by
    ///     <see cref="Use{TEntity,TRequest,TDetail,TSummary}(IList{string})" />.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type, decorated with <see cref="ResourceAttribute" />.</typeparam>
    /// <param name="endpoints">Optional endpoint names to restrict registration.</param>
    /// <param name="configure">Optional resource metadata callback.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataResourceBuilder AddResource<TEntity>(
        IList<string>?             endpoints = null,
        Action<ResourceAttribute>? configure = null
    )
        where TEntity : class, ICanonicalName {
        var entity = typeof(TEntity);

        var resource = entity.GetCustomAttribute<ResourceAttribute>()
                    ?? throw new InvalidOperationException(
                           $"Resource '{entity.FullName}' carries no [Resource] attribute. Either declare one, or "
                         + "register it with Use<TEntity, TRequest, TDetail, TSummary>() and name the DTO types.");

        return Register(resource, endpoints, configure);
    }

    private SchemataResourceBuilder Register(
        ResourceAttribute          resource,
        IList<string>?             endpoints,
        Action<ResourceAttribute>? configure
    ) {
        var input = resource;
        resource = new(input.Entity, input.Request, input.Detail, input.Summary) {
            Operations = input.Operations?.ToArray(),
            Methods = input.Methods?.Select(method => new ResourceMethodAttribute(method.Verb, method.Handler, method.Scope) {
                Method = method.Method,
            }).ToList(),
            AuthenticationScheme = input.AuthenticationScheme,
            DefaultPageSize = input.DefaultPageSize,
            MaxPageSize = input.MaxPageSize,
        };
        if (input.ConfiguredTotalSize is { } totalSize) resource.TotalSize = totalSize;

        resource.Endpoints = endpoints is null ? null
            : input.Endpoints is null ? new List<string>(endpoints)
            : input.Endpoints.Concat(endpoints).Distinct(StringComparer.Ordinal).ToList();

        configure?.Invoke(resource);

        resource.AuthenticationScheme ??= AuthenticationScheme;

        var methods = resource.Entity.GetCustomAttributes<ResourceMethodAttribute>().ToList();
        if (resource.Methods is not null) {
            methods.AddRange(resource.Methods);
        }

        Registry.Register(Services, resource, methods);
        return this;
    }

}
