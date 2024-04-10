using System;
using System.Collections.Generic;
using System.Reflection;
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

        if (endpoints != null) {
            resource.Endpoints.AddRange(endpoints);
        }

        builder.Builder.Configure<SchemataResourceOptions>(options => {
            options.Resources[resource.EntityType] = resource;
        });

        return builder;
    }
}
