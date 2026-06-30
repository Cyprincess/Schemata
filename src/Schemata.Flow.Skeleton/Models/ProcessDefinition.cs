using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Builders;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Strongly-typed BPMN process definition. Derived classes expose flow
///     elements as auto-properties; the base constructor instantiates the
///     null-valued ones via reflection.
/// </summary>
/// <remarks>
///     Magic-property discovery is adapted from Automatonymous: auto-properties of known types
///     (<see cref="Activity"/>, <see cref="StartEvent"/>, <see cref="EndEvent"/>, <see cref="FlowEvent"/>,
///     <see cref="Message"/>, <see cref="Message{TPayload}"/>, <see cref="Signal"/>,
///     <see cref="Signal{TPayload}"/>, <see cref="ErrorDefinition"/>,
///     <see cref="EscalationDefinition"/>) are instantiated (when null) and registered on the
///     definition collections by the base constructor. Element names default to the property
///     name (or <see cref="DisplayNameAttribute"/>), which makes them deterministic across
///     definition rebuilds — element names are the canonical identity persisted on tokens.
///     Pre-initialized properties keep their explicit name and are registered when absent;
///     a pre-initialized element that belongs inside a sub-process scope must be placed into
///     that scope's children before the base constructor runs, or not be exposed as a
///     magic property at all.
///     <para>
    ///         The <see cref="AllElements" />, <see cref="AllFlows" />, <see cref="ByName" />,
    ///         <see cref="OutgoingBySource" />, and <see cref="IncomingByTarget" /> properties
    ///         rebuild graph views from the mutable definition collections on each access.
///     </para>
/// </remarks>
public class ProcessDefinition
{
    /// <summary>Initializes a new <see cref="ProcessDefinition" /> and seeds known auto-properties via reflection.</summary>
    public ProcessDefinition() { InitializeProperties(); }

    /// <summary>Stable identifier of the process definition; serves as the lookup key in the registry.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Human-readable label surfaced in tooling and audit rows.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Free-form description of the process intent.</summary>
    public string? Description { get; set; }

    /// <summary>Every BPMN element discovered on the definition (activities, events, gateways).</summary>
    public List<FlowElement> Elements { get; } = [];

    /// <summary>Sequence flows connecting <see cref="Elements" />.</summary>
    public List<SequenceFlow> Flows { get; } = [];

    /// <summary>Activities that already have outgoing sequence flows during graph construction.</summary>
    internal HashSet<Activity> ActivitiesWithOutgoing { get; } = [];

    /// <summary>Head of the enter-task chain per activity; inbound edges route to it.</summary>
    internal Dictionary<Activity, ProcedureTask> EnterTasks { get; } = [];

    /// <summary>Returns the enter-task chain head when <paramref name="target" /> owns one, otherwise the target itself.</summary>
    internal FlowElement ResolveEntry(FlowElement target) {
        return target is Activity activity && EnterTasks.TryGetValue(activity, out var entry) ? entry : target;
    }

    /// <summary>Message definitions referenced by message events and tasks.</summary>
    public List<Message> Messages { get; } = [];

    /// <summary>Signal definitions referenced by signal events.</summary>
    public List<Signal> Signals { get; } = [];

    /// <summary>Error definitions referenced by error boundary events and end events.</summary>
    public List<ErrorDefinition> Errors { get; } = [];

    /// <summary>Escalation definitions referenced by escalation events.</summary>
    public List<EscalationDefinition> Escalations { get; } = [];

    private readonly List<FlowSourceDeclaration> _sources = [];

    /// <summary>
    ///     Source entity bindings declared on this definition. The registry merges
    ///     these into its source descriptor map so source-aware advisors run for message-driven
    ///     definitions that carry no source-typed guard condition.
    /// </summary>
    public IReadOnlyList<FlowSourceDeclaration> SourceDeclarations => _sources;

    /// <summary>
    ///     Declares a source entity type bound to this definition. The binding name defaults to
    ///     the underscored type name, matching the name the runtime writes when a process is
    ///     started with that source.
    /// </summary>
    /// <typeparam name="T">The bound source entity type.</typeparam>
    /// <param name="name">An explicit binding name; disambiguates multiple bindings of the same type.</param>
    /// <param name="projection">The projection mode applied to the binding.</param>
    protected void BindSource<T>(string? name = null, FlowSourceProjection? projection = null)
        where T : class, ICanonicalName {
        var binding = string.IsNullOrEmpty(name) ? typeof(T).Name.Underscore().ToLowerInvariant() : name;
        _sources.Add(new(binding, typeof(T), projection));
    }

    /// <summary>Declares a source binding that projects its state into <paramref name="state" />.</summary>
    /// <typeparam name="T">The bound source entity type.</typeparam>
    /// <param name="state">The source member receiving the projected state.</param>
    protected void BindSource<T>(Expression<Func<T, string?>> state)
        where T : class, ICanonicalName {
        var binding = typeof(T).Name.Underscore().ToLowerInvariant();
        _sources.Add(new(binding, typeof(T), StateMember: state));
    }

    /// <summary>Declares a named source binding that projects its state into <paramref name="state" />.</summary>
    /// <typeparam name="T">The bound source entity type.</typeparam>
    /// <param name="name">The source binding name.</param>
    /// <param name="state">The source member receiving the projected state.</param>
    protected void BindSource<T>(string name, Expression<Func<T, string?>> state)
        where T : class, ICanonicalName {
        var binding = string.IsNullOrEmpty(name) ? typeof(T).Name.Underscore().ToLowerInvariant() : name;
        _sources.Add(new(binding, typeof(T), StateMember: state));
    }

    /// <summary>Declares a named source binding configured by <paramref name="configure" />.</summary>
    /// <typeparam name="T">The bound source entity type.</typeparam>
    /// <param name="name">The source binding name.</param>
    /// <param name="configure">Configures the binding members and projection mode.</param>
    protected void BindSource<T>(string name, Action<FlowSourceBindingBuilder<T>> configure)
        where T : class, ICanonicalName {
        var builder = new FlowSourceBindingBuilder<T>();
        configure(builder);

        var binding = string.IsNullOrEmpty(name) ? typeof(T).Name.Underscore().ToLowerInvariant() : name;
        _sources.Add(new(binding, typeof(T), builder.ProjectionMode, builder.StateMember, builder.LifecycleMember));
    }

    /// <summary>Declares a convention-named source binding configured by <paramref name="configure" />.</summary>
    /// <typeparam name="T">The bound source entity type.</typeparam>
    /// <param name="configure">Configures the binding members and projection mode.</param>
    protected void BindSource<T>(Action<FlowSourceBindingBuilder<T>> configure)
        where T : class, ICanonicalName {
        var builder = new FlowSourceBindingBuilder<T>();
        configure(builder);

        var binding = typeof(T).Name.Underscore().ToLowerInvariant();
        _sources.Add(new(binding, typeof(T), builder.ProjectionMode, builder.StateMember, builder.LifecycleMember));
    }

    /// <summary>Every flow element visible from the root, including elements nested inside <see cref="SubProcess" /> children.</summary>
    public IReadOnlyList<FlowElement> AllElements {
        get {
            var list = new List<FlowElement>();
            CollectElements(Elements, list);
            return list;
        }
    }

    /// <summary>Every sequence flow visible from the root, including flows inside <see cref="SubProcess" /> children.</summary>
    public IReadOnlyList<SequenceFlow> AllFlows {
        get {
            var list = new List<SequenceFlow>(Flows);
            foreach (var sp in Elements.OfType<SubProcess>()) {
                CollectFlows(sp, list);
            }

            return list;
        }
    }

    /// <summary>Maps every flow element <see cref="FlowElement.Name" /> to its instance.</summary>
    public IReadOnlyDictionary<string, FlowElement> ByName {
        get {
            var dict = new Dictionary<string, FlowElement>(StringComparer.Ordinal);
            foreach (var e in AllElements) {
                if (!string.IsNullOrEmpty(e.Name) && !dict.ContainsKey(e.Name)) {
                    dict.Add(e.Name, e);
                }
            }

            return dict;
        }
    }

    /// <summary>
    ///     Maps every flow element to the sequence flows that originate from it.
    /// </summary>
    public IReadOnlyDictionary<FlowElement, IReadOnlyList<SequenceFlow>> OutgoingBySource =>
        BuildAdjacency(f => f.Source);

    /// <summary>
    ///     Maps every flow element to the sequence flows that target it.
    /// </summary>
    public IReadOnlyDictionary<FlowElement, IReadOnlyList<SequenceFlow>> IncomingByTarget =>
        BuildAdjacency(f => f.Target);

    private IReadOnlyDictionary<FlowElement, IReadOnlyList<SequenceFlow>> BuildAdjacency(
        Func<SequenceFlow, FlowElement> keySelector) {
        var dict = new Dictionary<FlowElement, List<SequenceFlow>>(ReferenceEqualityComparer.Instance);
        foreach (var flow in AllFlows) {
            var key = keySelector(flow);
            if (!dict.TryGetValue(key, out var list)) {
                list = [];
                dict[key] = list;
            }

            list.Add(flow);
        }

        var result = new Dictionary<FlowElement, IReadOnlyList<SequenceFlow>>(ReferenceEqualityComparer.Instance);
        foreach (var kv in dict) {
            result[kv.Key] = kv.Value.AsReadOnly();
        }

        return result;
    }

    private static void CollectElements(IEnumerable<FlowElement> source, List<FlowElement> dest) {
        foreach (var e in source) {
            dest.Add(e);
            if (e is SubProcess sp) {
                CollectElements(sp.Children, dest);
            }
        }
    }

    private static void CollectFlows(SubProcess sp, List<SequenceFlow> dest) {
        dest.AddRange(sp.ChildFlows);
        foreach (var nested in sp.Children.OfType<SubProcess>()) {
            CollectFlows(nested, dest);
        }
    }

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
            var displayName = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            var name        = displayName ?? prop.Name;

            if (prop.GetValue(this) is { } existing) {
                RegisterProperty(existing, name);
                continue;
            }

            var propType = prop.PropertyType;

            if (typeof(Activity).IsAssignableFrom(propType) && !propType.IsAbstract) {
                if (Activator.CreateInstance(propType) is not Activity activity) continue;

                activity.Name = name;

                SetPropertyValue(this, prop, activity);
                Elements.Add(activity);
            } else if (propType == typeof(StartEvent)) {
                var startEvent = new StartEvent { Name = name };

                SetPropertyValue(this, prop, startEvent);
                Elements.Add(startEvent);
            } else if (propType == typeof(EndEvent)) {
                var endEvent = new EndEvent { Name = name };

                SetPropertyValue(this, prop, endEvent);
                Elements.Add(endEvent);
            } else if (propType == typeof(FlowEvent)) {
                var flowEvent = new FlowEvent { Name = name };

                SetPropertyValue(this, prop, flowEvent);
                Elements.Add(flowEvent);
            } else if (typeof(Message).IsAssignableFrom(propType) && !propType.IsAbstract) {
                if (Activator.CreateInstance(propType) is not Message message) continue;

                message.Name = name;
                SetPropertyValue(this, prop, message);
                Messages.Add(message);
            } else if (typeof(Signal).IsAssignableFrom(propType) && !propType.IsAbstract) {
                if (Activator.CreateInstance(propType) is not Signal signal) continue;

                signal.Name = name;
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

    private void RegisterProperty(object value, string fallback) {
        switch (value) {
            case Activity activity:
                if (string.IsNullOrEmpty(activity.Name)) {
                    activity.Name = fallback;
                }

                if (!AllElements.Contains(activity)) {
                    Elements.Add(activity);
                }

                break;
            case FlowEvent flowEvent:
                if (string.IsNullOrEmpty(flowEvent.Name)) {
                    flowEvent.Name = fallback;
                }

                if (!AllElements.Contains(flowEvent)) {
                    Elements.Add(flowEvent);
                }

                break;
            case Message message:
                if (string.IsNullOrEmpty(message.Name)) {
                    message.Name = fallback;
                }

                if (!Messages.Contains(message)) {
                    Messages.Add(message);
                }

                break;
            case Signal signal:
                if (string.IsNullOrEmpty(signal.Name)) {
                    signal.Name = fallback;
                }

                if (!Signals.Contains(signal)) {
                    Signals.Add(signal);
                }

                break;
            case ErrorDefinition error:
                if (string.IsNullOrEmpty(error.Name)) {
                    error.Name = fallback;
                }

                if (!Errors.Contains(error)) {
                    Errors.Add(error);
                }

                break;
            case EscalationDefinition escalation:
                if (string.IsNullOrEmpty(escalation.Name)) {
                    escalation.Name = fallback;
                }

                if (!Escalations.Contains(escalation)) {
                    Escalations.Add(escalation);
                }

                break;
        }
    }
}

/// <summary>Declares source projection members for a binding on a <see cref="ProcessDefinition" />.</summary>
/// <param name="BindingName">The binding name the runtime uses to resolve the source.</param>
/// <param name="SourceType">The bound source entity CLR type.</param>
/// <param name="Projection">The explicit source projection mode, when configured.</param>
/// <param name="StateMember">The source member receiving the projected state, when configured.</param>
/// <param name="LifecycleMember">The source member receiving the projected lifecycle, when configured.</param>
public sealed record FlowSourceDeclaration(
    string                BindingName,
    Type                  SourceType,
    FlowSourceProjection? Projection      = null,
    LambdaExpression?     StateMember     = null,
    LambdaExpression?     LifecycleMember = null);
