using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Scheduling.Event;
using Schemata.Scheduling.Event.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate the Scheduling.Event feature.</summary>
public static class SchedulingEventBuilderExtensions
{
    /// <summary>Adds <c>SchemataSchedulingEventFeature</c> and applies the optional options delegate.</summary>
    public static SchemataBuilder UseSchedulingEvent(
        this SchemataBuilder                    builder,
        Action<SchemataSchedulingEventOptions>? configure = null
    ) {
        builder.AddFeature<SchemataSchedulingEventFeature>();
        if (configure != null) {
            builder.Services.Configure(configure);
        }

        return builder;
    }
}
