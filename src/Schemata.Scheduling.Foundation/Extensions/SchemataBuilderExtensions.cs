using System;
using Schemata.Core;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseScheduling(
        this SchemataBuilder       builder,
        Action<SchedulingBuilder>? configure = null
    ) {
        var schedulingBuilder = new SchedulingBuilder(builder.Services);
        configure?.Invoke(schedulingBuilder);

        builder.Configure<SchemataSchedulingOptions>(options => { options.Jobs.AddRange(schedulingBuilder.Jobs); });

        builder.AddFeature<SchemataSchedulingFeature>();

        return builder;
    }
}
