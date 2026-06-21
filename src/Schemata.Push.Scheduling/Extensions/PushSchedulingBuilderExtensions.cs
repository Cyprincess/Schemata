using Schemata.Push.Foundation.Builders;
using Schemata.Push.Scheduling.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataPushBuilder" /> extensions for the Push.Scheduling bridge.</summary>
public static class PushSchedulingBuilderExtensions
{
    /// <summary>
    ///     Registers <see cref="Schemata.Push.Scheduling.Features.SchemataPushSchedulingFeature" /> so
    ///     <c>IScheduledPushService.ScheduleSendAsync</c> defers delivery to a durable long-running
    ///     operation.
    /// </summary>
    /// <param name="builder">The push builder.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder UseScheduling(this SchemataPushBuilder builder) {
        builder.AddFeature<SchemataPushSchedulingFeature>();
        return builder;
    }
}
