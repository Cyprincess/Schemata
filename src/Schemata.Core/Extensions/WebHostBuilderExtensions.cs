using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Hosting;

public static class WebHostBuilderExtensions
{
    public static IWebHostBuilder UseSchemata(this IWebHostBuilder builder) {
        return UseSchemata(builder, _ => { }, _ => { });
    }

    public static IWebHostBuilder UseSchemata(this IWebHostBuilder builder, Action<SchemataBuilder>? schema) {
        return UseSchemata(builder, schema, _ => { });
    }

    public static IWebHostBuilder UseSchemata(
        this IWebHostBuilder     builder,
        Action<SchemataBuilder>? schema,
        Action<SchemataOptions>? configure) {
        builder.ConfigureServices((context, services) => {
            services.AddSchemata(context.Configuration, context.HostingEnvironment, schema, configure);
        });

        return builder;
    }
}
