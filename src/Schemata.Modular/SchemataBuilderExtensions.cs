// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            foreach (var provider in providers) {
                services.TryAddEnumerableSingleton(typeof(IModulesProvider), provider);
            }

            // To avoid accessing the builder.Configure() method and builder.ConfigureServices() method after building the service provider,
            // we create a runner here instead of in the delegate.
            var runner = DefaultModulesRunner.Create(builder.Options, // Avoid accessing the builder.Configure() method
                builder.Configuration,                                // and builder.ConfigureServices() method
                builder.Environment,                                  // after building the service provider.
                services);
            services.TryAddSingleton<IModulesRunner>(_ => runner);

            services.TryAddEnumerableSingleton<IStartupFilter, ModularStartup>(sp => ModularStartup.Create(
                builder.Options,       // Avoid accessing the builder.Configure() method
                builder.Configuration, // and builder.ConfigureServices() method
                builder.Environment,   // after building the service provider.
                sp                     //
            ));
        });

        return builder;
    }
}
