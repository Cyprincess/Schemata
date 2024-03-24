// ReSharper disable CheckNamespace

using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Schemata.Abstractions;
using Schemata.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchemata(
        this IServiceCollection services,
        IConfiguration          configuration,
        IWebHostEnvironment     environment) {
        return AddSchemata(services, configuration, environment, _ => { }, _ => { });
    }

    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataBuilder>? schema) {
        return AddSchemata(services, configuration, environment, schema, _ => { });
    }

    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataOptions>? configure) {
        return AddSchemata(services, configuration, environment, _ => { }, configure);
    }

    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataBuilder>? schema,
        Action<SchemataOptions>? configure) {
        services.AddTransient<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create( //
            configuration,                                                                  //
            environment                                                                     //
        ));

        var configurators = new Configurators();

        var options = new SchemataOptions();
        configure?.Invoke(options);

        var builder = new SchemataBuilder(services, configuration, environment, configurators, options);

        schema?.Invoke(builder);

        AddFeatures(builder);

        return builder.Build();
    }

    private static void AddFeatures(SchemataBuilder builder) {
        builder.ConfigureServices(services => {
            var modules = builder.Options.GetFeatures();
            if (modules is null) {
                return;
            }

            var features = modules.Values.ToList();

            features.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var feature in features) {
                feature.ConfigureServices(services, builder.Configurators, builder.Configuration, builder.Environment);
            }
        });
    }
}
