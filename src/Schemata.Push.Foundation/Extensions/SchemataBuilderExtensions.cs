using Schemata.Core;
using Schemata.Push.Foundation.Builders;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate the Push feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds <see cref="SchemataPushFeature{TPushSubscription}" /> closed on
    ///     <see cref="SchemataPushSubscription" /> and returns a <see cref="SchemataPushBuilder" />.
    ///     Use the returned builder to contribute transports.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder UsePush(this SchemataBuilder builder) {
        return builder.UsePush<SchemataPushSubscription>();
    }

    /// <summary>
    ///     Adds the Push feature closed on <typeparamref name="TPushSubscription" /> and returns a
    ///     <see cref="SchemataPushBuilder" />. Repositories and handlers operate only on the selected
    ///     subscription entity.
    /// </summary>
    /// <typeparam name="TPushSubscription">The persisted subscription entity type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The push builder for chaining.</returns>
    public static SchemataPushBuilder UsePush<TPushSubscription>(this SchemataBuilder builder)
        where TPushSubscription : SchemataPushSubscription, new() {
        builder.AddFeature<SchemataPushFeature<TPushSubscription>>();

        return new(builder.Options, builder.Services, typeof(TPushSubscription));
    }
}
