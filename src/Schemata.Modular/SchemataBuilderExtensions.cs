// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata;
using Schemata.Modular;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseModular(this SchemataBuilder builder) {
        return UseModular(builder, new[] { typeof(DefaultModulesProvider) });
    }

    public static SchemataBuilder UseModular<T>(this SchemataBuilder builder)
        where T : class, IModulesProvider {
        return UseModular(builder, new[] { typeof(T) });
    }

    public static SchemataBuilder UseModular(this SchemataBuilder builder, IEnumerable<Type> providers) {
        builder.ConfigureServices(services => {
            services.TryAddEnumerableSingleton<IPostConfigureOptions<SchemataOptions>, ModularPostConfigureOptions>();

            foreach (var provider in providers) {
                services.TryAddEnumerableSingleton(typeof(IModulesProvider), provider);
            }

            services.TryAddSingleton<IModulesRunner, DefaultModulesRunner>();
            services.TryAddEnumerableSingleton<IStartupFilter, ModularStartup>(sp => {
                var runner = sp.GetRequiredService<IModulesRunner>();
                return ModularStartup.Create(runner, builder.Configuration, builder.Environment);
            });

            using var sp     = services.BuildServiceProvider();
            var       runner = sp.GetRequiredService<IModulesRunner>();
            runner.ConfigureServices(services, builder.Configuration, builder.Environment, sp);
        });

        return builder;
    }
}
