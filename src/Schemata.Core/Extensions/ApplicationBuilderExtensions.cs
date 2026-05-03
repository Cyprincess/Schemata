using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for wiring the Schemata middleware pipeline and cleaning
///     up after configuration.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Invokes <see cref="ISimpleFeature.ConfigureApplication" /> for every
    ///     registered feature in priority order.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
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
    ///     Removes the feature dictionary from <see cref="SchemataOptions" /> after
    ///     pipeline configuration is complete, freeing memory for the remainder of
    ///     the application lifetime.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder CleanSchemata(this IApplicationBuilder app) {
        var sp = app.ApplicationServices;

        var schemata = sp.GetRequiredService<SchemataOptions>();

        schemata.Pop<Dictionary<RuntimeTypeHandle, ISimpleFeature>>(Keys.Features);

        return app;
    }
}
