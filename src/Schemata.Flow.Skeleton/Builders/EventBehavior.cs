using System;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>
///     Fluent builder for behavior attached to a <see cref="FlowEvent" />. <c>OnEnter</c> splices a
///     <see cref="ProcedureTask" /> in front of the event, so the body runs before the token settles
///     onto it (an end event completing, or a sequence intermediate event). This mirrors activity
///     <c>OnEnter</c> exactly: a real BPMN task node inserted into the sequence flow.
/// </summary>
public sealed class EventBehavior
{
    private readonly ProcessDefinition _definition;
    private readonly FlowEvent         _event;

    internal EventBehavior(ProcessDefinition definition, FlowEvent @event) {
        _definition = definition;
        _event      = @event;
    }

    /// <summary>Labels the event this builder configures.</summary>
    /// <param name="displayName">Human-readable event label.</param>
    /// <param name="description">Optional description of the event's role.</param>
    public EventBehavior Labelled(string displayName, string? description = null) {
        _event.Label(displayName, description);
        return this;
    }

    /// <summary>Labels the event for one language tag, filling its localized maps.</summary>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Display name for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />.</param>
    public EventBehavior Localized(string locale, string displayName, string? description = null) {
        _event.Localize(locale, displayName, description);
        return this;
    }

    /// <summary>Splices a procedure task running <paramref name="body" /> in front of the event.</summary>
    /// <param name="body">The delegate executed by the inserted procedure task.</param>
    public EventBehavior OnEnter(Func<FlowTaskContext, ValueTask> body) {
        _definition.InsertEnterTask(_event, new ProcedureTask { Name = $"Enter_{_event.Name}", Body = body });
        return this;
    }

    /// <summary>
    ///     Runs <paramref name="body" /> when a token enters the event, resolving the source bound
    ///     under the name derived from <typeparamref name="TSource" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public EventBehavior OnEnter<TSource>(Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        return OnEnter(FlowSourceDescriptor.DefaultBindingName<TSource>(), body);
    }

    /// <summary>
    ///     Runs <paramref name="body" /> when a token enters the event, resolving the source bound
    ///     under <paramref name="source" />.
    /// </summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="body">The delegate executed with the task context and the resolved source.</param>
    public EventBehavior OnEnter<TSource>(string source, Func<FlowTaskContext, TSource, ValueTask> body)
        where TSource : class, ICanonicalName {
        ArgumentException.ThrowIfNullOrEmpty(source);
        return OnEnter(FlowSourceBody.Bind(source, body));
    }
}
