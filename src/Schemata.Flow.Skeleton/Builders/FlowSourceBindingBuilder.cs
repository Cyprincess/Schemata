using System;
using System.Linq.Expressions;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Configures source members that receive state and lifecycle projections.</summary>
/// <typeparam name="T">The source entity type.</typeparam>
public sealed class FlowSourceBindingBuilder<T>
    where T : class, ICanonicalName
{
    internal Expression<Func<T, string?>>? StateMember { get; private set; }

    internal Expression<Func<T, string?>>? LifecycleMember { get; private set; }

    internal FlowSourceProjection? ProjectionMode { get; private set; }

    /// <summary>Selects the source member receiving the projected state.</summary>
    /// <param name="member">The writable source member.</param>
    /// <returns>This builder.</returns>
    public FlowSourceBindingBuilder<T> State(Expression<Func<T, string?>> member) {
        StateMember = member;
        return this;
    }

    /// <summary>Selects the source member receiving the projected lifecycle.</summary>
    /// <param name="member">The writable source member.</param>
    /// <returns>This builder.</returns>
    public FlowSourceBindingBuilder<T> Lifecycle(Expression<Func<T, string?>> member) {
        LifecycleMember = member;
        return this;
    }

    /// <summary>Selects the state projection mode.</summary>
    /// <param name="mode">The state projection mode.</param>
    /// <returns>This builder.</returns>
    public FlowSourceBindingBuilder<T> Projection(FlowSourceProjection mode) {
        ProjectionMode = mode;
        return this;
    }
}
