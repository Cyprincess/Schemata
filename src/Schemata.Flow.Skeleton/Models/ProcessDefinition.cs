using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Schemata.Flow.Skeleton.Models;

/// <remarks>
///     Magic-property discovery is adapted from Automatonymous:
///     null-valued auto-properties of known types (Activity, StartEvent,
///     EndEvent, FlowEvent, Message, Signal, ErrorDefinition, EscalationDefinition)
///     are instantiated via reflection in the base constructor.
/// </remarks>
public class ProcessDefinition
{
    public ProcessDefinition() { InitializeProperties(); }

    public string Name { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public List<FlowElement> Elements { get; } = [];

    public List<SequenceFlow> Flows { get; } = [];

    internal HashSet<Activity> ActivitiesWithOutgoing { get; } = [];

    public List<Message> Messages { get; } = [];

    public List<Signal> Signals { get; } = [];

    public List<ErrorDefinition> Errors { get; } = [];

    public List<EscalationDefinition> Escalations { get; } = [];

    private static void SetPropertyValue(object target, PropertyInfo prop, object? value) {
        if (prop.CanWrite) {
            prop.SetValue(target, value);
            return;
        }

        var backingField = target.GetType()
                                 .GetField(
                                      $"<{prop.Name}>k__BackingField",
                                      BindingFlags.Instance | BindingFlags.NonPublic
                                  );

        if (backingField is not null) {
            backingField.SetValue(target, value);
        }
    }

    private void InitializeProperties() {
        var type       = GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead);

        foreach (var prop in properties) {
            if (prop.GetValue(this) is not null) continue;

            var propType    = prop.PropertyType;
            var displayName = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            var name        = displayName ?? prop.Name;

            if (typeof(Activity).IsAssignableFrom(propType) && !propType.IsAbstract) {
                var activity = (Activity?)Activator.CreateInstance(propType);
                if (activity is null) continue;

                activity.Name = name;
                activity.Id   = GenerateId();

                SetPropertyValue(this, prop, activity);
                Elements.Add(activity);
            } else if (propType == typeof(StartEvent)) {
                var startEvent = new StartEvent { Name = name, Id = GenerateId() };

                SetPropertyValue(this, prop, startEvent);
                Elements.Add(startEvent);
            } else if (propType == typeof(EndEvent)) {
                var endEvent = new EndEvent { Name = name, Id = GenerateId() };

                SetPropertyValue(this, prop, endEvent);
                Elements.Add(endEvent);
            } else if (propType == typeof(FlowEvent)) {
                var flowEvent = new FlowEvent { Name = name, Id = GenerateId() };

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

    internal static string GenerateId() {
        return Guid.NewGuid().ToString("n");
    }
}
