using System;
using System.Collections.Generic;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Serializable result of adding a push subscription.</summary>
public sealed class PushSubscriptionResult
{
    /// <summary>The subscription unique identifier.</summary>
    public Guid Uid { get; init; }

    /// <summary>The subscription leaf name.</summary>
    public string? Name { get; init; }

    /// <summary>The full subscription canonical name.</summary>
    public string? CanonicalName { get; init; }

    /// <summary>The owner canonical name.</summary>
    public string? Owner { get; init; }

    /// <summary>The transport provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>The transport-specific endpoint identity.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>Transport-specific metadata.</summary>
    public Dictionary<string, string?>? Metadata { get; init; }

    /// <summary>Concurrency token.</summary>
    public Guid Timestamp { get; init; }

    /// <summary>The display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Localized display names.</summary>
    public Dictionary<string, string?>? DisplayNames { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>Localized descriptions.</summary>
    public Dictionary<string, string?>? Descriptions { get; init; }

    /// <summary>When the subscription is soft-deleted.</summary>
    public DateTime? DeleteTime { get; init; }

    /// <summary>When the subscription is permanently purged.</summary>
    public DateTime? PurgeTime { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTime? CreateTime { get; init; }

    /// <summary>When the subscription was last updated.</summary>
    public DateTime? UpdateTime { get; init; }

    /// <summary>Maps from a subscription entity.</summary>
    public static PushSubscriptionResult From(SchemataPushSubscription entity) => new()
    {
        Uid           = entity.Uid,
        Name          = entity.Name,
        CanonicalName = entity.CanonicalName,
        Owner         = entity.Owner,
        Provider      = entity.Provider,
        ProviderKey   = entity.ProviderKey,
        Metadata      = entity.Metadata,
        Timestamp     = entity.Timestamp,
        DisplayName   = entity.DisplayName,
        DisplayNames  = entity.DisplayNames,
        Description   = entity.Description,
        Descriptions  = entity.Descriptions,
        DeleteTime    = entity.DeleteTime,
        PurgeTime     = entity.PurgeTime,
        CreateTime    = entity.CreateTime,
        UpdateTime    = entity.UpdateTime,
    };

    /// <summary>Maps to a subscription entity.</summary>
    public SchemataPushSubscription ToEntity() => new()
    {
        Uid           = Uid,
        Name          = Name,
        CanonicalName = CanonicalName,
        Owner         = Owner,
        Provider      = Provider,
        ProviderKey   = ProviderKey,
        Metadata      = Metadata,
        Timestamp     = Timestamp,
        DisplayName   = DisplayName,
        DisplayNames  = DisplayNames,
        Description   = Description,
        Descriptions  = Descriptions,
        DeleteTime    = DeleteTime,
        PurgeTime     = PurgeTime,
        CreateTime    = CreateTime,
        UpdateTime    = UpdateTime,
    };
}
