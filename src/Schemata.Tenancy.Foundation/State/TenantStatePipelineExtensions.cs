using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Foundation.State;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Selects one native state branch from the services bound to each request.</summary>
public static class TenantStatePipelineExtensions
{
    /// <summary>
    ///     Inserts the selection middleware for <paramref name="pipeline" />. The current request services
    ///     supply the fixed host or tenant provider, so each request executes one native branch.
    /// </summary>
    /// <exception cref="InvalidOperationException">The pipeline is already wired on this application.</exception>
    public static IApplicationBuilder UseTenantStatePipeline(this IApplicationBuilder app, TenantStatePipeline pipeline) {
        ArgumentNullException.ThrowIfNull(app);

        var key = $"Schemata.Tenancy.StatePipeline.{pipeline}";
        if (app.Properties.ContainsKey(key)) {
            throw new InvalidOperationException($"Schemata tenant state pipeline '{pipeline}' is already wired on this application.");
        }

        app.Properties[key] = pipeline;

        if (app.ApplicationServices.GetService<ITenantStatePipelineProvider>() is DefaultTenantStatePipelineProvider provider) {
            provider.Initialize(new Dictionary<string, object?>(app.Properties));
        }

        app.Use((HttpContext context, RequestDelegate next) => {
            var state = context.RequestServices.GetRequiredService<ITenantStatePipelineProvider>();

            return state.Get(pipeline, next)(context);
        });

        return app;
    }
}
