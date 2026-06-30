using System;
using System.Linq;
using System.Xml;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Extension entry points for fluent <see cref="ProcessDefinition" /> construction.</summary>
public static class ProcessBuilder
{
    /// <summary>Begins a process with a plain (none) start event.</summary>
    public static StartFlow Start(this ProcessDefinition definition) { return new(definition); }

    /// <summary>Begins a process triggered by the given <paramref name="message" />.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="message">The message definition that starts the process.</param>
    public static StartFlow Start(this ProcessDefinition definition, Message message) {
        return new(definition, message);
    }

    /// <summary>Begins a process triggered by the given <paramref name="signal" />.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="signal">The signal definition that starts the process.</param>
    public static StartFlow Start(this ProcessDefinition definition, Signal signal) { return new(definition, signal); }

    /// <summary>Begins a process triggered by the given <paramref name="timer" /> definition.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="timer">The timer definition that starts the process.</param>
    public static StartFlow Start(this ProcessDefinition definition, TimerDefinition timer) {
        return new(definition, timer);
    }

    /// <summary>Begins a process triggered when the given <paramref name="condition" /> becomes true.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="condition">The conditional definition that starts the process.</param>
    public static StartFlow Start(this ProcessDefinition definition, ConditionalDefinition condition) {
        return new(definition, condition);
    }

    /// <summary>Opens a behavior builder for <paramref name="activity" />, defining its outgoing path and boundary events.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="activity">The activity whose outgoing behavior is being configured.</param>
    public static ActivityBehavior During(this ProcessDefinition definition, Activity activity) {
        return new(definition, activity);
    }

    /// <summary>Inserts a parallel join gateway that waits for all <paramref name="exits" /> to complete before continuing.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="exits">The parallel branches that must all arrive before the join fires.</param>
    public static ParallelJoin Join(this ProcessDefinition definition, params Activity[] exits) {
        var joinGateway = new ParallelGateway { Name = $"Join_{StableKey(exits)}" };
        definition.Elements.Add(joinGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(new() { Source = exit, Target = joinGateway });
        }

        return new(definition, joinGateway);
    }

    /// <summary>Inserts an inclusive merge gateway that waits for all active <paramref name="exits" /> to complete.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="exits">The inclusive branches that must all arrive before the merge fires.</param>
    public static InclusiveMerge Merge(this ProcessDefinition definition, params Activity[] exits) {
        var mergeGateway = new InclusiveGateway { Name = $"Merge_{StableKey(exits)}" };
        definition.Elements.Add(mergeGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(new() { Source = exit, Target = mergeGateway });
        }

        return new(definition, mergeGateway);
    }

    /// <summary>
    ///     Derives a deterministic gateway name suffix from the joined exits. Names are sorted
    ///     ordinally and length-prefixed so distinct exit sets can never produce the same key.
    /// </summary>
    private static string StableKey(Activity[] exits) {
        var parts = exits.Select(e => e.Name ?? string.Empty)
                         .OrderBy(n => n, StringComparer.Ordinal)
                         .Select(n => $"{n.Length}:{n}");
        return string.Join("_", parts);
    }

    /// <summary>Creates a branch guarded by a predicate over the default source binding.</summary>
    /// <typeparam name="T">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="predicate">The predicate evaluated against the typed variable value.</param>
    public static Branch When<T>(this ProcessDefinition definition, Func<T, bool> predicate)
        where T : class, ICanonicalName {
        return definition.When(typeof(T).Name.Underscore().ToLowerInvariant(), predicate);
    }

    /// <summary>Creates a branch guarded by a predicate over an explicitly named source binding.</summary>
    /// <typeparam name="T">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="predicate">The predicate evaluated against the typed variable value.</param>
    public static Branch When<T>(this ProcessDefinition definition, string source, Func<T, bool> predicate)
        where T : class, ICanonicalName {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return new(new NoneTask(), new SourceConditionExpression<T>(source, predicate));
    }

    /// <summary>Creates a branch guarded by a string expression over the default source binding.</summary>
    /// <typeparam name="T">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="expression">The expression text, compiled at registration with the configured Language.</param>
    public static Branch When<T>(this ProcessDefinition definition, string expression)
        where T : class, ICanonicalName {
        return definition.When<T>(typeof(T).Name.Underscore().ToLowerInvariant(), expression);
    }

    /// <summary>Creates a branch guarded by a string expression over an explicitly named source binding.</summary>
    /// <typeparam name="T">The source entity type resolved from the flow task context.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="expression">The expression text, compiled at registration with the configured Language.</param>
    public static Branch When<T>(this ProcessDefinition definition, string source, string expression)
        where T : class, ICanonicalName {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(expression);

        return new(new NoneTask(), new SourceStringConditionExpression<T>(source, expression));
    }

    /// <summary>Creates a branch guarded by a source and typed message payload predicate.</summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="message">The message definition whose payload type is accepted by the predicate.</param>
    /// <param name="predicate">The predicate evaluated against the source entity and payload.</param>
    public static Branch When<TSource, TPayload>(
        this ProcessDefinition definition,
        Message<TPayload>      message,
        Func<TSource, TPayload, bool> predicate
    ) where TSource : class, ICanonicalName {
        return definition.When(typeof(TSource).Name.Underscore().ToLowerInvariant(), message, predicate);
    }

    /// <summary>Creates a branch guarded by an explicitly named source and typed message payload predicate.</summary>
    /// <typeparam name="TSource">The source entity type resolved from the flow task context.</typeparam>
    /// <typeparam name="TPayload">The message payload type.</typeparam>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="source">The source binding name; disambiguates multiple bindings of the same CLR type.</param>
    /// <param name="message">The message definition whose payload type is accepted by the predicate.</param>
    /// <param name="predicate">The predicate evaluated against the source entity and payload.</param>
    public static Branch When<TSource, TPayload>(
        this ProcessDefinition definition,
        string                 source,
        Message<TPayload>      message,
        Func<TSource, TPayload, bool> predicate
    ) where TSource : class, ICanonicalName {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return new(new NoneTask(), new SourcePayloadConditionExpression<TSource, TPayload>(source, predicate));
    }

    /// <summary>Creates a branch guarded by an <see cref="IConditionExpression" />.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="condition">The condition expression that must evaluate to true for this branch to be taken.</param>
    public static Branch When(this ProcessDefinition definition, IConditionExpression condition) {
        return new(new NoneTask(), condition);
    }

    /// <summary>Creates the default branch taken when no other condition in a gateway matches.</summary>
    /// <param name="definition">The process definition being built.</param>
    public static Branch Otherwise(this ProcessDefinition definition) {
        return new(new NoneTask(), isDefault: true);
    }

    /// <summary>Creates an event-based gateway branch that fires when <paramref name="message" /> is received.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="message">The message definition to listen for.</param>
    public static EventBranch On(this ProcessDefinition definition, Message message) { return new(message); }

    /// <summary>Creates an event-based gateway branch that fires when <paramref name="signal" /> is received.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="signal">The signal definition to listen for.</param>
    public static EventBranch On(this ProcessDefinition definition, Signal signal) { return new(signal); }

    /// <summary>Creates an event-based gateway branch that fires after <paramref name="duration" /> elapses.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="duration">The elapsed time before this branch fires.</param>
    public static EventBranch OnTimer(this ProcessDefinition definition, TimeSpan duration) {
        return new(new TimerDefinition {
            Name = $"Timer_{duration}", TimerType = TimerType.Duration, TimeExpression = XmlConvert.ToString(duration),
        });
    }

    /// <summary>Creates an event-based gateway branch that fires when <paramref name="condition" /> becomes true.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="condition">The conditional definition to evaluate.</param>
    public static EventBranch OnCondition(this ProcessDefinition definition, ConditionalDefinition condition) {
        return new(condition);
    }
}
