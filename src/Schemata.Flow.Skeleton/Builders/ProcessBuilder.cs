using System;
using System.Text.Json;
using System.Xml;
using Humanizer;
using Schemata.Common;
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
        var joinGateway = new ParallelGateway { Id = $"gateway_{Identifiers.NewUid():n}", Name = "Join" };
        definition.Elements.Add(joinGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = exit, Target = joinGateway,
            });
        }

        return new(definition, joinGateway);
    }

    /// <summary>Inserts an inclusive merge gateway that waits for all active <paramref name="exits" /> to complete.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="exits">The inclusive branches that must all arrive before the merge fires.</param>
    public static InclusiveMerge Merge(this ProcessDefinition definition, params Activity[] exits) {
        var mergeGateway = new InclusiveGateway { Id = $"gateway_{Identifiers.NewUid():n}", Name = "Merge" };
        definition.Elements.Add(mergeGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = exit, Target = mergeGateway,
            });
        }

        return new(definition, mergeGateway);
    }

    public static Branch When<T>(this ProcessDefinition definition, Func<T, bool> predicate)
        where T : class {
        var key = typeof(T).Name.Underscore().ToLowerInvariant();

        return new(new NoneTask { Name = "branch", Id = Identifiers.NewUid().ToString("n") },
                   new LambdaConditionExpression {
                       Lambda = ctx => {
                           if (!ctx.Variables.TryGetValue(key, out var value)) {
                               return new(false);
                           }

                           // Condition variables may arrive from legacy payloads with casing that differs
                           // from the process model, so bind with the case-insensitive shared options.
                           var entity = value switch {
                               T t              => t,
                               JsonElement json => JsonSerializer.Deserialize<T>(json.GetRawText(), SchemataJson.Default),
                               var _            => null,
                           };

                           return new(entity is not null && predicate(entity));
                       },
                   });
    }

    /// <summary>Creates a branch guarded by an <see cref="IConditionExpression" />.</summary>
    /// <param name="definition">The process definition being built.</param>
    /// <param name="condition">The condition expression that must evaluate to true for this branch to be taken.</param>
    public static Branch When(this ProcessDefinition definition, IConditionExpression condition) {
        return new(new NoneTask { Name = "branch", Id = Identifiers.NewUid().ToString("n") }, condition);
    }

    /// <summary>Creates the default branch taken when no other condition in a gateway matches.</summary>
    /// <param name="definition">The process definition being built.</param>
    public static Branch Otherwise(this ProcessDefinition definition) {
        return new(new NoneTask { Name = "branch", Id = Identifiers.NewUid().ToString("n") }, isDefault: true);
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
