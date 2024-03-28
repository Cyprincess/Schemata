using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder UseSchemata(this WebApplicationBuilder builder) {
        return UseSchemata(builder, _ => { }, _ => { });
    }

    public static WebApplicationBuilder UseSchemata(
        this WebApplicationBuilder builder,
        Action<SchemataBuilder>?   schema) {
        return UseSchemata(builder, schema, _ => { });
    }

    public static WebApplicationBuilder UseSchemata(
        this WebApplicationBuilder builder,
        Action<SchemataBuilder>?   schema,
        Action<SchemataOptions>?   configure) {
        builder.Services.AddSchemata(builder.Configuration, builder.Environment, schema, configure);

        return builder;
    }
}
