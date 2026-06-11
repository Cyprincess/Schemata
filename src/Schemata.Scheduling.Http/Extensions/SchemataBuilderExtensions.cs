using Schemata.Core;
using Schemata.Scheduling.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate Scheduling HTTP resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingHttpFeature" />.</summary>
    public static SchemataBuilder UseSchedulingHttp(this SchemataBuilder builder) {
        builder.AddFeature<SchemataSchedulingHttpFeature>();

        return builder;
    }
}
