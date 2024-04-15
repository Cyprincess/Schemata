using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Schemata.Abstractions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataResourceBuilder Use<TEntity>(this SchemataResourceBuilder builder) {
        return Use(builder, typeof(TEntity));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest>(this SchemataResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest), typeof(TDetail));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary));
    }

    public static SchemataResourceBuilder Use(
        this SchemataResourceBuilder        builder,
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

        builder.Builder.Configure<SchemataResourceOptions>(options => {
            options.Resources[resource.Entity] = resource;
        });

        return builder;
    }
}
