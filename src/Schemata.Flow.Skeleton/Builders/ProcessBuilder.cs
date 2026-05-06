using System;
using System.Text.Json;
using Humanizer;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

public static class ProcessBuilder
{
    public static StartFlow Start(this ProcessDefinition definition) { return new(definition); }

    public static StartFlow Start(this ProcessDefinition definition, Message message) {
        return new(definition, message);
    }

    public static StartFlow Start(this ProcessDefinition definition, Signal signal) { return new(definition, signal); }

    public static StartFlow Start(this ProcessDefinition definition, TimerDefinition timer) {
        return new(definition, timer);
    }

    public static StartFlow Start(this ProcessDefinition definition, ConditionalDefinition condition) {
        return new(definition, condition);
    }

    public static ActivityBehavior During(this ProcessDefinition definition, Activity activity) {
        return new(definition, activity);
    }

    public static ParallelJoin Join(this ProcessDefinition definition, params Activity[] exits) {
        var joinGateway = new ParallelGateway { Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = "Join" };
        definition.Elements.Add(joinGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = exit, Target = joinGateway,
                }
            );
        }

        return new(definition, joinGateway);
    }

    public static InclusiveMerge Merge(this ProcessDefinition definition, params Activity[] exits) {
        var mergeGateway = new InclusiveGateway { Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = "Merge" };
        definition.Elements.Add(mergeGateway);

        foreach (var exit in exits) {
            definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = exit, Target = mergeGateway,
                }
            );
        }

        return new(definition, mergeGateway);
    }

    public static Branch When<T>(this ProcessDefinition definition, Func<T, bool> predicate)
        where T : class {
        var key     = typeof(T).Name.Underscore().ToLowerInvariant();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        return new(
            new NoneTask { Name = "branch", Id = ProcessDefinition.GenerateId() },
            new LambdaConditionExpression {
                Lambda = ctx => {
                    if (!ctx.Variables.TryGetValue(key, out var value)) {
                        return new(false);
                    }

                    var entity = value switch {
                        T t              => t,
                        JsonElement json => JsonSerializer.Deserialize<T>(json.GetRawText(), options),
                        _                => default(T),
                    };

                    return new(entity is not null && predicate(entity));
                },
            }
        );
    }

    public static Branch When(this ProcessDefinition definition, IConditionExpression condition) {
        return new(new NoneTask { Name = "branch", Id = ProcessDefinition.GenerateId() }, condition);
    }

    public static Branch Otherwise(this ProcessDefinition definition) {
        return new(new NoneTask { Name = "branch", Id = ProcessDefinition.GenerateId() }, isDefault: true);
    }

    public static EventBranch On(this ProcessDefinition definition, Message message) { return new(message); }

    public static EventBranch On(this ProcessDefinition definition, Signal signal) { return new(signal); }

    public static EventBranch OnTimer(this ProcessDefinition definition, TimeSpan duration) {
        return new(
            new TimerDefinition {
                Name = $"Timer_{duration}", TimerType = TimerType.Duration, TimeExpression = duration.ToString(),
            }
        );
    }

    public static EventBranch OnCondition(this ProcessDefinition definition, ConditionalDefinition condition) {
        return new(condition);
    }
}
