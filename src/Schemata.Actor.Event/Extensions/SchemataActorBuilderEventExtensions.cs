using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Event;
using Schemata.Actor.Event.Features;
using Schemata.Actor.Event.Handlers;
using Schemata.Actor.Foundation;
using Schemata.Event.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataActorBuilder" /> extensions for the Actor.Event bridge.</summary>
public static class SchemataActorBuilderEventExtensions
{
    /// <summary>Enables the <see cref="SchemataActorEventFeature" />.</summary>
    /// <param name="builder">The actor builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataActorBuilder UseEvent(this SchemataActorBuilder builder) {
        builder.AddFeature<SchemataActorEventFeature>();

        return builder;
    }

    /// <summary>
    ///     Registers <typeparamref name="TRoute" /> as an <see cref="IEventActorRoute{TEvent}" /> for
    ///     <typeparamref name="TEvent" />, along with the internal <see cref="IEventHandler{TEvent}" />
    ///     that forwards every matched event to the actor it resolves. Calling this for a second route
    ///     on the same <typeparamref name="TEvent" /> adds it alongside the first - every registered
    ///     route for the event type gets its own delivery attempt.
    /// </summary>
    /// <typeparam name="TEvent">The event type to route.</typeparam>
    /// <typeparam name="TRoute">The route implementation resolving the target actor.</typeparam>
    /// <param name="builder">The actor builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataActorBuilder RouteEvent<TEvent, TRoute>(this SchemataActorBuilder builder)
        where TEvent : IEvent
        where TRoute : class, IEventActorRoute<TEvent> {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventActorRoute<TEvent>, TRoute>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<TEvent>, EventActorForwarder<TEvent>>());

        return builder;
    }
}
