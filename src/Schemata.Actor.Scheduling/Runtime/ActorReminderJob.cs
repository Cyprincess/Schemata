using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Actor.Skeleton;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Actor.Scheduling.Runtime;

/// <summary>
///     Fires a durable actor reminder armed by <see cref="ActorReminders" />: reconstructs the
///     target <see cref="ActorId" /> and the serialized payload from
///     <see cref="JobContext.Variables" />, then delivers it through <c>IActorRef.TellAsync</c>. A
///     single registered job type serves every reminder - what changes per fire travels entirely in
///     the variables <see cref="ActorReminders" /> wrote when it scheduled this occurrence.
/// </summary>
[ScheduledJob(JobKey)]
public sealed class ActorReminderJob(IActorSystem actors, IServiceProvider services) : IScheduledJob
{
    /// <summary>Stable scheduler key persisted on every actor-reminder job and execution row.</summary>
    public const string JobKey = "schemata.actor.reminder";

    /// <summary>Variable key carrying the target <see cref="ActorId.Type" />.</summary>
    internal const string ActorTypeVariable = "actorType";

    /// <summary>Variable key carrying the target <see cref="ActorId.Key" />.</summary>
    internal const string ActorKeyVariable = "actorKey";

    /// <summary>Variable key carrying the payload's <see cref="Type.FullName" />, resolved back through <see cref="AppDomainTypeCache" />.</summary>
    internal const string PayloadTypeVariable = "payloadType";

    /// <summary>Variable key carrying the payload serialized with <see cref="SchemataJson.Default" />.</summary>
    internal const string PayloadJsonVariable = "payloadJson";

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var target  = new ActorId(RequireVariable(context, ActorTypeVariable), RequireVariable(context, ActorKeyVariable));
        var payload = DeserializePayload(context);

        // Captured from this job's own execution scope, so ambient state restored on the actor's
        // turn matches what was in effect when the reminder fired, not when it was scheduled.
        var messageContext = MessageContexts.Capture(services);

        var actor = await actors.GetAsync(target);
        await actor.TellAsync(payload, messageContext, ct);
    }

    #endregion

    private static IMessage DeserializePayload(JobContext context) {
        var typeName = RequireVariable(context, PayloadTypeVariable);
        var payloadType = AppDomainTypeCache.GetType(typeName)
            ?? throw new FailedPreconditionException(message: $"Actor reminder payload type '{typeName}' could not be resolved.");

        var json = RequireVariable(context, PayloadJsonVariable);
        return JsonSerializer.Deserialize(json, payloadType, SchemataJson.Default) as IMessage
            ?? throw new FailedPreconditionException(message: $"Actor reminder payload of type '{typeName}' could not be deserialized.");
    }

    private static string RequireVariable(JobContext context, string name) {
        if (context.Variables.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)) {
            return value;
        }

        throw new FailedPreconditionException(message: $"Actor reminder job execution is missing required variable '{name}'.");
    }
}
