using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Features;

namespace Schemata.Resource.Foundation;

public sealed class SchemataResourceBuilder
{
    public SchemataResourceBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    private SchemataOptions Schemata { get; }

    private IServiceCollection Services { get; }

    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature(typeof(T));
    }

    public SchemataResourceBuilder WithAuthorization() {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(AdviceListRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<>), typeof(AdviceGetRequestAuthorize<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestAuthorize<,>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<>), typeof(AdviceDeleteRequestAuthorize<>)));

        return this;
    }

    public SchemataResourceBuilder WithoutCreateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressCreateValidation = true);
        return this;
    }

    public SchemataResourceBuilder WithoutUpdateValidation() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressUpdateValidation = true);
        return this;
    }

    public SchemataResourceBuilder WithoutFreshness() {
        Services.Configure<SchemataResourceOptions>(o => o.SuppressFreshness = true);
        return this;
    }

    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        string?        package   = null,
        IList<string>? endpoints = null
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
            resource.Endpoints = endpoints;
        }

        if (package is not null) {
            resource.Package = package;
        }

        SchemataResourceFeature.RegisterResource(Services, resource);

        return this;
    }
}
