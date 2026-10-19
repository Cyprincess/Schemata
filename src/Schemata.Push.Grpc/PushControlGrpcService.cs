using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Models;

namespace Schemata.Push.Grpc;

[Authorize]
public sealed class PushControlGrpcService(IRequestDispatcher dispatcher) : IPushControlService
{
    public async ValueTask<PushSubscriptionView> CreateAsync(CreatePushSubscriptionCommand request, CallContext context = default) {
        var result = await dispatcher.SendAsync<CreatePushControlRequest, PushSubscriptionInfo>(
            new(context.ServerCallContext!.GetHttpContext().User, request.Provider, request.ProviderKey,
                request.Metadata.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)), context.CancellationToken);
        return PushSubscriptionView.From(result);
    }

    public async ValueTask<ListPushSubscriptionsResult> ListAsync(ListPushSubscriptionsQuery request, CallContext context = default) {
        var subscriptions = await dispatcher.SendAsync<ListPushControlRequest, IReadOnlyList<PushSubscriptionInfo>>(
            new(context.ServerCallContext!.GetHttpContext().User, request.Provider), context.CancellationToken);
        var result = new ListPushSubscriptionsResult();
        foreach (var subscription in subscriptions) result.Subscriptions.Add(PushSubscriptionView.From(subscription));
        return result;
    }

    public async ValueTask<DeletePushSubscriptionResult> DeleteAsync(DeletePushSubscriptionCommand request, CallContext context = default) {
        _ = await dispatcher.SendAsync<DeletePushControlRequest, Unit>(
            new(context.ServerCallContext!.GetHttpContext().User, request.Provider, request.ProviderKey), context.CancellationToken);
        return new();
    }

    public async ValueTask<SendPushResult> SendAsync(SendPushCommand request, CallContext context = default) {
        JsonElement message = default;
        if (request.MessageJson is { } source) {
            try {
                using var document = JsonDocument.Parse(source);
                message = document.RootElement.Clone();
            } catch (JsonException) {
                throw new InvalidArgumentException(args: new Dictionary<string, string?> { ["field"] = "message_json" });
            }
        }
        if (request.Options is { InvalidDuration: true }) {
            throw new InvalidArgumentException(args: new Dictionary<string, string?> { ["field"] = "options.time_to_live" });
        }
        PushControlTarget? target = request.TargetKind == PushTargetKind.Unspecified ? null : new(
            request.TargetKind switch {
                PushTargetKind.Broadcast => "broadcast",
                PushTargetKind.Topic => "topic",
                PushTargetKind.Channel => "channel",
                PushTargetKind.Recipient => "recipient",
                PushTargetKind.Custom => "custom",
                _ => ((int)request.TargetKind).ToString(CultureInfo.InvariantCulture),
            }, request.Topic, request.Channel, request.Subject, request.CustomKind,
            request.CustomParams.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        var options = request.Options is { } wire ? new PushControlOptions(wire.Priority, wire.TimeToLive, wire.CollapseKey, wire.DedupId) : null;
        var results = await dispatcher.SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(
            new(context.ServerCallContext!.GetHttpContext().User, message, target, options,
                request.Metadata.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)), context.CancellationToken);
        var result = new SendPushResult();
        foreach (var outcome in results) result.Outcomes.Add(TransportOutcome.From(outcome));
        return result;
    }
}
