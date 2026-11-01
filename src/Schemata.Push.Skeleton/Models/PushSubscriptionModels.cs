using System;
using System.Collections.Generic;
using System.Text.Json;
using Schemata.Push.Skeleton.Control;

namespace Schemata.Push.Skeleton.Models;

/// <summary>Wire request creating a subscription. The owner is derived from the caller.</summary>
public sealed class CreatePushSubscriptionRequest
{
    /// <summary>The transport provider name.</summary>
    public string? Provider { get; set; }

    /// <summary>The transport-specific endpoint identity; input-only and never returned.</summary>
    public string? ProviderKey { get; set; }

    /// <summary>Transport-specific metadata; input-only and never returned.</summary>
    public Dictionary<string, string?>? Metadata { get; set; }
}

/// <summary>Wire request deleting one subscription by its addressing identity.</summary>
public sealed class DeletePushSubscriptionRequest
{
    /// <summary>The transport provider name.</summary>
    public string? Provider { get; set; }

    /// <summary>The transport-specific endpoint identity.</summary>
    public string? ProviderKey { get; set; }
}

/// <summary>Wire view of a subscription with provider credentials omitted.</summary>
public sealed class PushSubscriptionInfo
{
    /// <summary>The subscription unique identifier.</summary>
    public Guid Uid { get; set; }

    /// <summary>The full subscription canonical name.</summary>
    public string? CanonicalName { get; set; }


    /// <summary>The transport provider name.</summary>
    public string? Provider { get; set; }

    /// <summary>When the subscription was created.</summary>
    public DateTime? CreateTime { get; set; }

    /// <summary>When the subscription was last updated.</summary>
    public DateTime? UpdateTime { get; set; }

    public static PushSubscriptionInfo From(Entities.SchemataPushSubscription subscription) => new() {
        Uid           = subscription.Uid,
        CanonicalName = subscription.CanonicalName,
        Provider      = subscription.Provider,
        CreateTime    = subscription.CreateTime,
        UpdateTime    = subscription.UpdateTime,
    };
}

/// <summary>Wire request for immediate fan-out through every registered transport.</summary>
public sealed class SendPushWireRequest
{
    /// <summary>An arbitrary JSON value; an absent property remains Undefined.</summary>
    public JsonElement Message { get; set; }

    /// <summary>The dispatch target; defaults to a broadcast when omitted.</summary>
    public PushControlTarget? Target { get; set; }

    /// <summary>Cross-transport delivery options.</summary>
    public PushControlOptions? Options { get; set; }

    /// <summary>Transport-specific metadata keyed by transport-defined names.</summary>
    public Dictionary<string, string?>? Metadata { get; set; }
}
