using System;
using System.Collections.Generic;
using ProtoBuf;
using ProtoBuf.WellKnownTypes;
using Schemata.Push.Skeleton.Models;
using Schemata.Push.Skeleton;

namespace Schemata.Push.Grpc;

/// <summary>Wire view of one subscription with provider credentials omitted.</summary>
[ProtoContract]
public sealed class PushSubscriptionView
{
    /// <summary>The subscription unique identifier.</summary>
    [ProtoMember(1)]
    public string Uid { get; set; } = string.Empty;

    /// <summary>The full subscription canonical name.</summary>
    [ProtoMember(2)]
    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>The transport provider name.</summary>
    [ProtoMember(3)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>When the subscription was created.</summary>
    [ProtoMember(4)]
    public DateTime? CreateTime { get; set; }

    /// <summary>When the subscription was last updated.</summary>
    [ProtoMember(5)]
    public DateTime? UpdateTime { get; set; }

    internal static PushSubscriptionView From(PushSubscriptionInfo subscription) => new() {
        Uid           = subscription.Uid.ToString(),
        CanonicalName = subscription.CanonicalName ?? string.Empty,
        Provider      = subscription.Provider ?? string.Empty,
        CreateTime    = subscription.CreateTime,
        UpdateTime    = subscription.UpdateTime,
    };
}

/// <summary>Wire request creating a subscription; the owner is derived from the caller.</summary>
[ProtoContract]
public sealed class CreatePushSubscriptionCommand
{
    /// <summary>The transport provider name.</summary>
    [ProtoMember(1)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>The transport-specific endpoint identity; input-only and never returned.</summary>
    [ProtoMember(2)]
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Transport-specific metadata; input-only and never returned.</summary>
    [ProtoMember(3)]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>Wire request listing the calling owner's subscriptions.</summary>
[ProtoContract]
public sealed class ListPushSubscriptionsQuery
{
    /// <summary>The optional transport provider to filter on.</summary>
    [ProtoMember(1)]
    public string? Provider { get; set; }
}

/// <summary>Wire response listing subscriptions without credentials.</summary>
[ProtoContract]
public sealed class ListPushSubscriptionsResult
{
    /// <summary>The calling owner's subscriptions.</summary>
    [ProtoMember(1)]
    public List<PushSubscriptionView> Subscriptions { get; set; } = [];
}

/// <summary>Wire request deleting one subscription by its addressing identity.</summary>
[ProtoContract]
public sealed class DeletePushSubscriptionCommand
{
    /// <summary>The transport provider name.</summary>
    [ProtoMember(1)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>The transport-specific endpoint identity.</summary>
    [ProtoMember(2)]
    public string ProviderKey { get; set; } = string.Empty;
}

/// <summary>Wire response confirming removal; carries no payload.</summary>
[ProtoContract]
public sealed class DeletePushSubscriptionResult;


/// <summary>Selects the dispatch target shape; fields of the chosen shape must be set.</summary>
public enum PushTargetKind
{
    /// <summary>No target chosen; defaults to broadcast.</summary>
    Unspecified,

    /// <summary>Every connection a transport holds.</summary>
    Broadcast,

    /// <summary>A publish/subscribe topic; set <see cref="SendPushCommand.Topic" />.</summary>
    Topic,

    /// <summary>A named channel; set <see cref="SendPushCommand.Channel" />.</summary>
    Channel,

    /// <summary>One recipient canonical name; set <see cref="SendPushCommand.Subject" />.</summary>
    Recipient,

    /// <summary>A transport-defined kind; set <see cref="SendPushCommand.CustomKind" />.</summary>
    Custom,
}

/// <summary>Wire request for immediate fan-out through every registered transport.</summary>
[ProtoContract]
public sealed class SendPushCommand
{
    /// <summary>An arbitrary JSON value encoded as JSON text; absence is rejected by the control handler.</summary>
    [ProtoMember(1)]
    public string? MessageJson { get; set; }

    /// <summary>The dispatch target shape.</summary>
    [ProtoMember(2)]
    public PushTargetKind TargetKind { get; set; }

    /// <summary>The topic for <see cref="PushTargetKind.Topic" />.</summary>
    [ProtoMember(3)]
    public string? Topic { get; set; }

    /// <summary>The channel for <see cref="PushTargetKind.Channel" />.</summary>
    [ProtoMember(4)]
    public string? Channel { get; set; }

    /// <summary>The recipient canonical name for <see cref="PushTargetKind.Recipient" />.</summary>
    [ProtoMember(5)]
    public string? Subject { get; set; }

    /// <summary>The transport-defined kind for <see cref="PushTargetKind.Custom" />.</summary>
    [ProtoMember(6)]
    public string? CustomKind { get; set; }

    /// <summary>Opaque parameters passed with a custom target.</summary>
    [ProtoMember(7)]
    public Dictionary<string, string> CustomParams { get; set; } = new();

    /// <summary>Presence preserves an explicitly selected Low priority.</summary>
    [ProtoMember(8)]
    public SendPushOptions? Options { get; set; }

    /// <summary>Transport-specific metadata keyed by transport-defined names.</summary>
    [ProtoMember(12)]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

[ProtoContract(Serializer = typeof(PushOptionsSerializer))]
public sealed class SendPushOptions
{
    internal bool InvalidDuration { get; set; }
    internal Duration? RawDuration { get; set; }

    [ProtoMember(1)]
    public PushPriority? Priority { get; set; }

    [ProtoMember(2)]
    public TimeSpan? TimeToLive { get; set; }

    [ProtoMember(3)]
    public string? CollapseKey { get; set; }

    [ProtoMember(4)]
    public string? DedupId { get; set; }
}

/// <summary>Outcome reported by one transport for one dispatch.</summary>
[ProtoContract]
public sealed class TransportOutcome
{
    /// <summary>The reporting transport's name.</summary>
    [ProtoMember(1)]
    public string Transport { get; set; } = string.Empty;

    /// <summary>The delivery outcome.</summary>
    [ProtoMember(2)]
    public TransportStatus Status { get; set; }

    /// <summary>The obfuscated delivery address, when applicable.</summary>
    [ProtoMember(3)]
    public string? Address { get; set; }

    /// <summary>The backend-assigned message reference, when applicable.</summary>
    [ProtoMember(4)]
    public string? Provider { get; set; }

    /// <summary>The failure reason when delivery failed.</summary>
    [ProtoMember(5)]
    public string? Error { get; set; }

    internal static TransportOutcome From(Skeleton.TransportResult result) => new() {
        Transport = result.Transport,
        Status    = result.Status,
        Address   = result.Address,
        Provider  = result.Provider,
        Error     = result.Error,
    };
}

/// <summary>Wire response reporting one outcome per transport.</summary>
[ProtoContract]
public sealed class SendPushResult
{
    /// <summary>One outcome per registered transport, preserving partial failures.</summary>
    [ProtoMember(1)]
    public List<TransportOutcome> Outcomes { get; set; } = [];
}
