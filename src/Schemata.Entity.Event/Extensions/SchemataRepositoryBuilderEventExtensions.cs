using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Event.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to publish entity-buffered
///     events after a successful commit.
/// </summary>
public static class SchemataRepositoryBuilderEventExtensions
{
    /// <summary>
    ///     Registers the committed advisor that drains
    ///     <see cref="Schemata.Event.Skeleton.IHasPendingEvents" /> entities onto the event bus.
    /// </summary>
    /// <remarks>
    ///     Registered through <c>TryAddEnumerable</c> with an open generic: the advisor chain is a
    ///     collection, and a plain <c>AddScoped(typeof(...))</c> would silently replace it rather
    ///     than join it.
    ///     <para>
    ///         The container must supply some <see cref="Schemata.Event.Skeleton.IEventBus" />
    ///         implementation. <c>Schemata.Event.Foundation</c> is one, but not the only one — this
    ///         package depends on the contract, not on that implementation.
    ///     </para>
    /// </remarks>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseEvent(this SchemataRepositoryBuilder builder) {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IRepositoryCommittedAdvisor<>), typeof(AdviceCommittedPendingEvents<>)));

        return builder;
    }
}
