using System;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataHttpResourceBuilderExtensions
{
    public static SchemataHttpResourceBuilder Use<TEntity>(this SchemataHttpResourceBuilder builder) {
        return Use(builder, typeof(TEntity));
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest>(this SchemataHttpResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest));
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataHttpResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest), typeof(TDetail));
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataHttpResourceBuilder builder) {
        return Use(builder, typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary));
    }

    public static SchemataHttpResourceBuilder Use(
        this SchemataHttpResourceBuilder builder,
        Type                             entity,
        Type?                            request = null,
        Type?                            detail  = null,
        Type?                            summary = null) {
        builder.Builder.Use(entity, request, detail, summary, [new HttpResourceAttribute()]);
        return builder;
    }
}
