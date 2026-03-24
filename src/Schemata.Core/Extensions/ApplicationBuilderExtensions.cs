using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for configuring the Schemata middleware pipeline on <see cref="IApplicationBuilder" />.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Configures the Schemata middleware pipeline by invoking all registered features.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemata(
        this IApplicationBuilder app,
        IConfiguration           configuration,
        IWebHostEnvironment      environment
    ) {
        var sp = app.ApplicationServices;

        var schemata = sp.GetRequiredService<SchemataOptions>();

        var modules = schemata.GetFeatures();
        if (modules is null) {
            return app;
        }

        var features = modules.Values.ToList();

        features.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var feature in features) {
            feature.ConfigureApplication(app, configuration, environment);
        }

        return app;
    }

    /// <summary>
    ///     Removes the features dictionary from Schemata options after pipeline configuration is complete.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder CleanSchemata(this IApplicationBuilder app) {
        var sp = app.ApplicationServices;

        var schemata = sp.GetRequiredService<SchemataOptions>();

        schemata.Pop<Dictionary<RuntimeTypeHandle, ISimpleFeature>>(SchemataConstants.Options.Features);

        return app;
    }
}
