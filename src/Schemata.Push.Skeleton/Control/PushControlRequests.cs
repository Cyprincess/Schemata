using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton.Models;

namespace Schemata.Push.Skeleton.Control;

public sealed record CreatePushControlRequest(
    ClaimsPrincipal? Principal,
    string? Provider,
    string? ProviderKey,
    Dictionary<string, string?>? Metadata = null) : IRequest<PushSubscriptionInfo>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record ListPushControlRequest(
    ClaimsPrincipal? Principal,
    string? Provider = null) : IRequest<IReadOnlyList<PushSubscriptionInfo>>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record DeletePushControlRequest(
    ClaimsPrincipal? Principal,
    string? Provider,
    string? ProviderKey) : IRequest<Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record SendPushControlRequest(
    ClaimsPrincipal? Principal,
    JsonElement Message,
    PushControlTarget? Target = null,
    PushControlOptions? Options = null,
    Dictionary<string, string?>? Metadata = null) : IRequest<ImmutableArray<TransportResult>>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record PushControlTarget(
    string? Kind,
    string? Topic = null,
    string? Channel = null,
    string? Subject = null,
    string? CustomKind = null,
    Dictionary<string, string?>? Params = null);

public sealed record PushControlOptions(
    PushPriority? Priority = null,
    TimeSpan? TimeToLive = null,
    string? CollapseKey = null,
    string? DedupId = null);
