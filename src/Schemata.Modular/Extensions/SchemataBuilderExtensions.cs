using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Modular;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseModular(this SchemataBuilder builder) {
        return UseModular<DefaultModulesRunner>(builder);
    }

    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder)
        where TRunner : class, IModulesRunner {
        return UseModular(builder, typeof(TRunner), new[] { typeof(DefaultModulesProvider) });
    }

    public static SchemataBuilder UseModular<TRunner>(this SchemataBuilder builder, IEnumerable<Type> providers)
        where TRunner : class, IModulesRunner {
        return UseModular(builder, typeof(TRunner), providers);
    }

    public static SchemataBuilder UseModular(this SchemataBuilder builder, Type runner, IEnumerable<Type> providers) {
        builder.ConfigureServices(services => {
            var modules = providers
                         .Select(p => Utilities.CreateInstance<IModulesProvider>(p, builder.CreateLogger(p),
                              builder.Configuration, builder.Environment))
                         .OfType<IModulesProvider>()
                         .SelectMany(p => p.GetModules())
                         .ToList();
            builder.GetOptions().SetModules(modules);

            if (services.All(s => s.ServiceType != typeof(IModulesRunner))) {
                // To avoid accessing the builder.Configure() method and builder.ConfigureServices() method after building the service provider,
                // we create a runner here instead of in the delegate.

                var run = Utilities.CreateInstance<IModulesRunner>(runner, builder.CreateLogger(runner),
                    builder.GetOptions())!;
                run.ConfigureServices(services, builder.Configuration, builder.Environment);
                services.TryAddSingleton<IModulesRunner>(_ => run);
            }

            services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, ModularStartup>(sp => ModularStartup.Create(
                builder.Configuration,
                builder.Environment,
                sp
            )));
        });

        return builder;
    }
}
