// ReSharper disable CheckNamespace

using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata;

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
        this IWebHostBuilder builder,
        Action<SchemataBuilder>? schema,
        Action<SchemataOptions>? configure) {
        builder.ConfigureServices((context, services) => {
            services.TryAddEnumerableSingleton<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create( // 
                context.Configuration, // avoid using dependency injection
                context.HostingEnvironment // to resolve IConfiguration and IWebHostEnvironment
            ));

            services.AddSchemata(context.Configuration, context.HostingEnvironment, schema, configure);
        });

        return builder;
    }
}
