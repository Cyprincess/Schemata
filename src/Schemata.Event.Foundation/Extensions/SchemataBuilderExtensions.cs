using Schemata.Core;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder"/> extensions that enable the event subsystem.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Registers <see cref="SchemataEventFeature"/> and returns an <see cref="EventBuilder"/> for further configuration.</summary>
    public static EventBuilder UseEvent(this SchemataBuilder builder) {
        builder.AddFeature<SchemataEventFeature>();

        return new(builder.Services);
    }
}
