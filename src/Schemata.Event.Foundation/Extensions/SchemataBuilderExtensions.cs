using System;
using Schemata.Core;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseEvent(this SchemataBuilder builder, Action<EventBuilder>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataEventFeature>();

        return builder;
    }
}
