using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Schemata.Common;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Strongly-typed BPMN process definition. Derived classes expose flow
///     elements as auto-properties; the base constructor instantiates the
///     null-valued ones via reflection.
/// </summary>
/// <remarks>
///     Magic-property discovery is adapted from Automatonymous: null-valued
///     auto-properties of known types (<see cref="Activity"/>,
///     <see cref="StartEvent"/>, <see cref="EndEvent"/>, <see cref="FlowEvent"/>,
///     <see cref="Message"/>, <see cref="Signal"/>, <see cref="ErrorDefinition"/>,
///     <see cref="EscalationDefinition"/>) are instantiated via reflection in
///     the base constructor.
/// </remarks>
public class ProcessDefinition
{
    /// <summary>Initializes a new <see cref="ProcessDefinition"/> and seeds known auto-properties via reflection.</summary>
    public ProcessDefinition() { InitializeProperties(); }

    /// <summary>Stable identifier of the process definition; serves as the lookup key in the registry.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Human-readable label surfaced in tooling and audit rows.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Free-form description of the process intent.</summary>
    public string? Description { get; set; }

    /// <summary>Every BPMN element discovered on the definition (activities, events, gateways).</summary>
    public List<FlowElement> Elements { get; } = [];

    /// <summary>Sequence flows connecting <see cref="Elements"/>.</summary>
    public List<SequenceFlow> Flows { get; } = [];

    internal HashSet<Activity> ActivitiesWithOutgoing { get; } = [];

    /// <summary>Message definitions referenced by message events and tasks.</summary>
    public List<Message> Messages { get; } = [];

    /// <summary>Signal definitions referenced by signal events.</summary>
    public List<Signal> Signals { get; } = [];

    /// <summary>Error definitions referenced by error boundary events and end events.</summary>
    public List<ErrorDefinition> Errors { get; } = [];

    /// <summary>Escalation definitions referenced by escalation events.</summary>
    public List<EscalationDefinition> Escalations { get; } = [];

    private static void SetPropertyValue(object target, PropertyInfo prop, object? value) {
        if (prop.CanWrite) {
            prop.SetValue(target, value);
            return;
        }

        var backingField = target.GetType()
                                 .GetField($"<{prop.Name}>k__BackingField",
                                           BindingFlags.Instance | BindingFlags.NonPublic);

        if (backingField is not null) {
            backingField.SetValue(target, value);
        }
    }

    private void InitializeProperties() {
        var type       = GetType();
        var properties = AppDomainTypeCache.GetProperties(type).Values.Where(p => p.CanRead);

        foreach (var prop in properties) {
            if (prop.GetValue(this) is not null) continue;

            var propType    = prop.PropertyType;
            var displayName = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            var name        = displayName ?? prop.Name;

            if (typeof(Activity).IsAssignableFrom(propType) && !propType.IsAbstract) {
                var activity = (Activity?)Activator.CreateInstance(propType);
                if (activity is null) continue;

                activity.Name = name;
                activity.Id   = Identifiers.NewUid().ToString("n");

                SetPropertyValue(this, prop, activity);
                Elements.Add(activity);
            } else if (propType == typeof(StartEvent)) {
                var startEvent = new StartEvent { Name = name, Id = Identifiers.NewUid().ToString("n") };

                SetPropertyValue(this, prop, startEvent);
                Elements.Add(startEvent);
            } else if (propType == typeof(EndEvent)) {
                var endEvent = new EndEvent { Name = name, Id = Identifiers.NewUid().ToString("n") };

                SetPropertyValue(this, prop, endEvent);
                Elements.Add(endEvent);
            } else if (propType == typeof(FlowEvent)) {
                var flowEvent = new FlowEvent { Name = name, Id = Identifiers.NewUid().ToString("n") };

                SetPropertyValue(this, prop, flowEvent);
                Elements.Add(flowEvent);
            } else if (propType == typeof(Message)) {
                var message = new Message { Name = name };
                SetPropertyValue(this, prop, message);
                Messages.Add(message);
            } else if (propType == typeof(Signal)) {
                var signal = new Signal { Name = name };
                SetPropertyValue(this, prop, signal);
                Signals.Add(signal);
            } else if (propType == typeof(ErrorDefinition)) {
                var error = new ErrorDefinition { Name = name };
                SetPropertyValue(this, prop, error);
                Errors.Add(error);
            } else if (propType == typeof(EscalationDefinition)) {
                var escalation = new EscalationDefinition { Name = name };
                SetPropertyValue(this, prop, escalation);
                Escalations.Add(escalation);
            }
        }
    }
}
