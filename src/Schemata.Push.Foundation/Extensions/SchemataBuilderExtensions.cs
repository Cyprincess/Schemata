using Schemata.Core;
using Schemata.Push.Foundation.Builders;
using Schemata.Push.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate the Push feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds <see cref="SchemataPushFeature" /> and returns a <see cref="SchemataPushBuilder" />.
    ///     Use the returned builder (or the optional callback) to contribute transports.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder UsePush(this SchemataBuilder builder) {
        builder.AddFeature<SchemataPushFeature>();

        return new(builder.Options, builder.Services);
    }
}
