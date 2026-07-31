using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Await" /> waiting on an event definition.</summary>
public sealed class EventBranch : IDescriptive
{
    private readonly IEventDefinition                  _eventDefinition;
    private          Branch[]?                         _decisionBranches;
    private          Func<FlowTaskContext, ValueTask>? _onEnter;
    private          FlowElement?                      _target;

    internal EventBranch(IEventDefinition eventDefinition) { _eventDefinition = eventDefinition; }

    /// <summary>Label carried onto the synthesized catch event.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Localized display names carried onto the synthesized catch event.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Description carried onto the synthesized catch event.</summary>
    public string? Description { get; set; }

    /// <summary>Localized descriptions carried onto the synthesized catch event.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }

    /// <summary>
    ///     Splices a procedure task running <paramref name="body" /> onto the catch's outgoing edge;
    ///     the body runs when the catch fires and the token passes through the inserted sequence-flow node.
    /// </summary>
    /// <param name="body">The delegate executed by the inserted procedure task when the catch fires.</param>
    public EventBranch OnEnter(Func<FlowTaskContext, ValueTask> body) {
        _onEnter = body;
        return this;
    }

    /// <summary>Routes the branch to <paramref name="target" /> when the event fires.</summary>
    public EventBranch Go(FlowElement target) {
        if (_decisionBranches is not null) {
            throw new InvalidOperationException($"Cannot set target on event branch '{
                _eventDefinition.Name
            }' after Decide has been called.");
        }

        _target = target;
        return this;
    }

    /// <summary>Routes the branch to <paramref name="target" />.</summary>
    public EventBranch Go(Activity target) { return Go((FlowElement)target); }

    /// <summary>Routes the branch to <paramref name="target" />.</summary>
    public EventBranch Go(EndEvent target) { return Go((FlowElement)target); }

    /// <summary>
    ///     Labels the synthesized catch event. The event has no declaration site to carry
    ///     <c>[DisplayName]</c>, so this is its only label channel.
    /// </summary>
    /// <param name="displayName">Human-readable event label.</param>
    /// <param name="description">Optional description of what the branch waits for.</param>
    public EventBranch Labelled(string displayName, string? description = null) {
        this.Label(displayName, description);
        return this;
    }

    /// <summary>Labels the synthesized catch event for one language tag.</summary>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Event label for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />.</param>
    public EventBranch Localized(string locale, string displayName, string? description = null) {
        this.Localize(locale, displayName, description);
        return this;
    }

    /// <summary>Inserts an exclusive gateway after the catch event with the supplied <paramref name="branches" />.</summary>
    public EventBranch Decide(params Branch[] branches) {
        if (_target is not null) {
            throw new InvalidOperationException($"Cannot call Decide on event branch '{
                _eventDefinition.Name
            }' after Go has been called.");
        }

        _decisionBranches = branches;
        return this;
    }

    /// <summary>
    ///     Adds the catch event and outgoing branch flows to <paramref name="definition" />.
    ///     The catch name is scoped by the owning gateway so two catches on the same event
    ///     definition under different gateways stay distinct.
    /// </summary>
    internal void Build(ProcessDefinition definition, EventBasedGateway gateway) {
        var catchEvent = new FlowEvent {
            Name       = $"Catch_{gateway.Name}_{_eventDefinition.Name}",
            Position   = EventPosition.IntermediateCatch,
            Definition = _eventDefinition,
        };

        this.CopyLabels(catchEvent);
        definition.Elements.Add(catchEvent);
        definition.Flows.Add(new() { Source = gateway, Target = catchEvent });

        FlowElement source = catchEvent;
        if (_onEnter is not null) {
            var task = new ProcedureTask { Name = $"Enter_{catchEvent.Name}", Body = _onEnter };
            definition.Elements.Add(task);
            definition.Flows.Add(new() { Source = catchEvent, Target = task });
            source = task;
        }

        if (_decisionBranches is not null) {
            var exclusiveGw = new ExclusiveGateway { Name = $"Decision_{catchEvent.Name}" };
            definition.Elements.Add(exclusiveGw);

            definition.Flows.Add(new() { Source = source, Target = exclusiveGw });

            for (var i = 0; i < _decisionBranches.Length; i++) {
                var branch = _decisionBranches[i];
                branch.EnsureExitRegistered(definition, exclusiveGw, i);
                var edge = new SequenceFlow {
                    Source    = exclusiveGw,
                    Target    = definition.ResolveEntry(branch.Exit),
                    Condition = branch.Condition,
                    IsDefault = branch.IsDefault,
                };

                branch.CopyLabels(edge);
                definition.Flows.Add(edge);
            }
        } else if (_target is not null) {
            definition.Flows.Add(new() { Source = source, Target = definition.ResolveEntry(_target) });
        }
    }
}
