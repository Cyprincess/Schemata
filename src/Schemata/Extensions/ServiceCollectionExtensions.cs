// ReSharper disable CheckNamespace

using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata;
using Schemata.Features;

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
        var builder = new SchemataBuilder(services, configuration, environment);

        builder.Configure(configure);

        schema?.Invoke(builder);

        AddFeatures(builder);

        return builder.Build();
    }

    private static void AddFeatures(SchemataBuilder builder) {
        builder.ConfigureServices(services => {
            using var sp = services.BuildServiceProvider();

            var options = sp.GetRequiredService<IOptions<SchemataOptions>>().Value;

            var modules = options.GetFeatures();
            if (modules is null) return;

            var features = modules.Select(m => (ISimpleFeature)ActivatorUtilities.CreateInstance(sp, m)!).ToList();

            features.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var feature in features) {
                feature.ConfigureServices(services, builder.Configuration, builder.Environment);
            }
        });
    }
}
