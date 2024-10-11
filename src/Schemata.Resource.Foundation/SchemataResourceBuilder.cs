using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advices;
using Schemata.Resource.Foundation.Security;

namespace Schemata.Resource.Foundation;

public sealed class SchemataResourceBuilder
{
    public SchemataResourceBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    public SchemataResourceBuilder(IServiceCollection services) {
        Services = services;
    }

    private SchemataOptions? Schemata { get; }

    private IServiceCollection? Services { get; }

    public void AddFeature<T>() where T : ISimpleFeature {
        Schemata?.AddFeature(typeof(T));
    }

    public SchemataResourceBuilder WithAuthorization() {
        Services?.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvice<>), typeof(AdviceListRequestAuthorize<>)));
        Services?.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvice<>), typeof(AdviceGetRequestAuthorize<>)));
        Services?.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvice<,>), typeof(AdviceCreateRequestAuthorize<,>)));
        Services?.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditRequestAdvice<,>), typeof(AdviceEditRequestAuthorize<,>)));
        Services?.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvice<>), typeof(AdviceDeleteRequestAuthorize<>)));

        return this;
    }

    public SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(IEnumerable<ResourceAttributeBase>? endpoints = null) {
        var entity  = typeof(TEntity);
        var request = typeof(TRequest);
        var detail  = typeof(TDetail);
        var summary = typeof(TSummary);

        var resource = entity.GetCustomAttribute<ResourceAttribute>() ?? new(entity, request, detail, summary);

        if (endpoints is not null) {
            resource.Endpoints.AddRange(endpoints);
        }

        Services?.AddAccessProvider<TEntity, ResourceRequestContext<long>, ResourceAccessProvider<TEntity, long>>();
        Services?.AddAccessProvider<TEntity, ResourceRequestContext<ListRequest>, ResourceAccessProvider<TEntity, ListRequest>>();
        Services?.AddAccessProvider<TEntity, ResourceRequestContext<TRequest>, ResourceAccessProvider<TEntity, TRequest>>();

        Services?.Configure<SchemataResourceOptions>(options => { options.Resources[resource.Entity.TypeHandle] = resource; });

        return this;
    }
}
