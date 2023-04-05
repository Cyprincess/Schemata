// ReSharper disable CheckNamespace

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Schemata;

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
        Action<SchemataBuilder>? schema,
        Action<SchemataOptions>? configure) {
        builder.Services.TryAddEnumerableSingleton<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create( // 
            builder.Configuration, // avoid using dependency injection
            builder.Environment // to resolve IConfiguration and IWebHostEnvironment
        ));

        builder.Services.AddSchemata(builder.Configuration, builder.Environment, schema, configure);

        return builder;
    }
}
