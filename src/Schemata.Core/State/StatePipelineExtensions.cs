using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.State;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Wires the state branch selection point. Per request the middleware
///     resolves the current <see cref="IStatePipelineProvider" /> from the bound
///     request services and executes the branch it returns, so one request runs
///     exactly one native state branch.
/// </summary>
public static class StatePipelineExtensions
{
    /// <summary>
    ///     Inserts the state branch selection middleware for
    ///     <paramref name="pipeline" />. Wiring one pipeline twice on one
    ///     application is an observable duplicate registration and fails here;
    ///     applications that also insert the native <c>UseSession</c> /
    ///     <c>UseAntiforgery</c> middleware directly are on an unsupported layout.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="pipeline">The state pipeline to branch per request.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="pipeline" /> is already wired on <paramref name="app" />.
    /// </exception>
    public static IApplicationBuilder UseStatePipeline(this IApplicationBuilder app, StatePipeline pipeline) {
        ArgumentNullException.ThrowIfNull(app);

        var key = $"Schemata.StatePipeline.{pipeline}";
        if (app.Properties.ContainsKey(key)) {
            throw new InvalidOperationException($"Schemata state pipeline '{pipeline}' is already wired on this application.");
        }

        app.Properties[key] = pipeline;

        if (app.ApplicationServices.GetService<IStatePipelineProvider>() is DefaultStatePipelineProvider provider) {
            provider.Initialize(new Dictionary<string, object?>(app.Properties));
        }

        app.Use((HttpContext context, RequestDelegate next) => {
            var state = context.RequestServices.GetRequiredService<IStatePipelineProvider>();

            return state.Get(pipeline, next)(context);
        });

        return app;
    }
}
