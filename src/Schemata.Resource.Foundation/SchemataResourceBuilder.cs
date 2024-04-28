using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Resource.Foundation;

public sealed class SchemataResourceBuilder
{
    public SchemataResourceBuilder(SchemataOptions schemata, Configurators configurators) {
        Schemata      = schemata;
        Configurators = configurators;
    }

    public SchemataResourceBuilder(IServiceCollection services) {
        Services = services;
    }

    private SchemataOptions? Schemata { get; }

    private Configurators? Configurators { get; }

    private IServiceCollection? Services { get; }

    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata?.AddFeature(typeof(T));
    }

    public SchemataResourceBuilder Use(
        Type                                entity,
        Type?                               request   = null,
        Type?                               detail    = null,
        Type?                               summary   = null,
        IEnumerable<ResourceAttributeBase>? endpoints = null) {
        var resource = entity.GetCustomAttribute<ResourceAttribute>() ?? new(entity, request, detail, summary);

        if (endpoints is not null) {
            resource.Endpoints.AddRange(endpoints);
        }

        var authorize = resource.Entity.GetCustomAttribute<AuthorizeAttribute>();
        if (authorize is not null) {
            var policy = new ResourcePolicyAttribute {
                Methods = string.Join(",", [
                    nameof(resource.List), nameof(resource.Get), nameof(resource.Create),
                    nameof(resource.Update), nameof(resource.Delete),
                ]),
                Policy                = authorize.Policy,
                Roles                 = authorize.Roles,
                AuthenticationSchemes = authorize.AuthenticationSchemes,
            };
            resource.List   ??= policy;
            resource.Get    ??= policy;
            resource.Create ??= policy;
            resource.Update ??= policy;
            resource.Delete ??= policy;
        }

        if (Configurators is not null) {
            Configurators.Set<SchemataResourceOptions>(options => {
                options.Resources[resource.Entity] = resource;
            });
        } else if (Services is not null) {
            Services.Configure<SchemataResourceOptions>(options => {
                options.Resources[resource.Entity] = resource;
            });
        } else {
            throw new NullReferenceException();
        }

        return this;
    }
}
