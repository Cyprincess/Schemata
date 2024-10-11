using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;

// ReSharper disable once CheckNamespace
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
        var builder = new SchemataBuilder(configuration, environment);

        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create(configuration, environment)));

        services.TryAddSingleton(builder.Options);

        schema?.Invoke(builder);
        configure?.Invoke(builder.Options);

        builder.Invoke(services);

        return services;
    }
}
