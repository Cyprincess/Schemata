using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Entities;
using Schemata.Push.Skeleton.Models;

namespace Schemata.Push.Foundation.Handlers;

public sealed class PushControlHandler(
    IAuthorizationService authorization,
    IPushOwnerResolver owners,
    IRequestDispatcher dispatcher)
    : IRequestHandler<CreatePushControlRequest, PushSubscriptionInfo>,
      IRequestHandler<ListPushControlRequest, IReadOnlyList<PushSubscriptionInfo>>,
      IRequestHandler<DeletePushControlRequest, Unit>,
      IRequestHandler<SendPushControlRequest, ImmutableArray<TransportResult>>
{
    public async Task<PushSubscriptionInfo> HandleAsync(CreatePushControlRequest request, CancellationToken ct = default) {
        await AuthorizeAsync(request.Principal, PushPolicies.Create);
        var owner = Owner(request.Principal);
        var subscription = await dispatcher.SendAsync<AddPushSubscriptionRequest, SchemataPushSubscription>(
            new(owner, Required(request.Provider, "provider"), Required(request.ProviderKey, "provider_key"), request.Metadata), ct);
        return PushSubscriptionInfo.From(subscription);
    }

    public async Task<IReadOnlyList<PushSubscriptionInfo>> HandleAsync(ListPushControlRequest request, CancellationToken ct = default) {
        await AuthorizeAsync(request.Principal, PushPolicies.List);
        var owner = Owner(request.Principal);
        var subscriptions = await dispatcher.SendAsync<GetPushSubscriptionsQuery, IReadOnlyList<SchemataPushSubscription>>(
            new(owner, request.Provider), ct);
        var result = new List<PushSubscriptionInfo>(subscriptions.Count);
        foreach (var subscription in subscriptions) {
            result.Add(PushSubscriptionInfo.From(subscription));
        }
        return result;
    }

    public async Task<Unit> HandleAsync(DeletePushControlRequest request, CancellationToken ct = default) {
        await AuthorizeAsync(request.Principal, PushPolicies.Delete);
        var owner = Owner(request.Principal);
        return await dispatcher.SendAsync<RemovePushSubscriptionRequest, Unit>(
            new(owner, Required(request.Provider, "provider"), Required(request.ProviderKey, "provider_key")), ct);
    }

    public async Task<ImmutableArray<TransportResult>> HandleAsync(SendPushControlRequest request, CancellationToken ct = default) {
        await AuthorizeAsync(request.Principal, PushPolicies.Send);
        if (request.Message.ValueKind == JsonValueKind.Undefined) throw Invalid("message");
        var target = Target(request.Target);
        var options = Options(request.Options);
        var context = new PushContext(request.Message, target) {
            Options = options,
            Metadata = request.Metadata ?? (IReadOnlyDictionary<string, string?>)ImmutableDictionary<string, string?>.Empty,
        };
        return await dispatcher.SendAsync<SendPushRequest, ImmutableArray<TransportResult>>(new(context), ct);
    }

    private async Task AuthorizeAsync(ClaimsPrincipal? principal, string policy) {
        if (principal is null) throw new UnauthenticatedException();
        var result = await authorization.AuthorizeAsync(principal, policy);
        if (!result.Succeeded) throw new PermissionDeniedException();
    }

    private string Owner(ClaimsPrincipal? principal) => owners.Resolve(principal)
        ?? throw new UnauthenticatedException();

    private static string Required(string? value, string field) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw Invalid(field);

    private static PushTarget Target(PushControlTarget? target) => target switch {
        null => new BroadcastTarget(),
        { Kind: "broadcast" } => new BroadcastTarget(),
        { Kind: "topic" } => new TopicTarget(Required(target.Topic, "target.topic")),
        { Kind: "channel" } => new ChannelTarget(Required(target.Channel, "target.channel")),
        { Kind: "recipient" } => new RecipientTarget(Required(target.Subject, "target.subject")),
        { Kind: "custom" } => new CustomTarget(Required(target.CustomKind, "target.custom_kind"),
            target.Params ?? (IReadOnlyDictionary<string, string?>)ImmutableDictionary<string, string?>.Empty),
        _ => throw Invalid("target.kind"),
    };

    private static PushOptions Options(PushControlOptions? options) {
        if (options is null) return PushOptions.Default;
        var priority = options.Priority ?? PushOptions.Default.Priority;
        if (!Enum.IsDefined(priority)) throw Invalid("options.priority");
        return new PushOptions {
            Priority = priority,
            TimeToLive = options.TimeToLive,
            CollapseKey = options.CollapseKey,
            DedupId = options.DedupId,
        };
    }

    private static InvalidArgumentException Invalid(string field) => new(args: new Dictionary<string, string?> { ["field"] = field });
}
