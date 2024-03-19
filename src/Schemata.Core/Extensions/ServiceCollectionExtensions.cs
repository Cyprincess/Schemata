// ReSharper disable CheckNamespace

using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection TryAddEnumerableSingleton<T, TI>(
        this IServiceCollection    services,
        Func<IServiceProvider, TI> factory)
        where T : class
        where TI : class, T {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<T, TI>(factory));
        return services;
    }

    public static IServiceCollection TryAddEnumerableSingleton<T, TI>(this IServiceCollection services)
        where T : class
        where TI : class, T {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<T, TI>());
        return services;
    }

    public static IServiceCollection TryAddEnumerableSingleton(
        this IServiceCollection services,
        Type                    service,
        Type                    implementation) {
        services.TryAddEnumerable(ServiceDescriptor.Singleton(service, implementation));
        return services;
    }

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
        services.TryAddEnumerableSingleton<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create(
            configuration, //
            environment    //
        ));

        var options = new SchemataOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var builder = new SchemataBuilder(services, configuration, environment, options);

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

            var features = modules.ToList();

            features.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var feature in features) {
                feature.ConfigureServices(services, builder.Configurators, builder.Configuration, builder.Environment);
            }
        });
    }
}
