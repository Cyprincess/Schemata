using System;

namespace Schemata.Event.Skeleton;

/// <summary>
///     A persisted event replayed to the broker by the outbox dispatcher. Carries the
///     already-serialized payload, routing metadata, and the optional optimistic-snapshot
///     reference to the originating business entity for direct broker replay.
/// </summary>
/// <param name="EventType">Wire-format event type, doubling as the routing key.</param>
/// <param name="Payload">Serialized event body.</param>
/// <param name="CorrelationId">Correlation identifier carried end-to-end.</param>
/// <param name="SourceType">
///     CLR full name of the business entity that produced the event, or <see langword="null" />
///     for source-free events.
/// </param>
/// <param name="Source">
///     Canonical resource name of the source business entity, or <see langword="null" />.
/// </param>
/// <param name="SourceTimestamp">
///     Concurrency timestamp captured from the source business entity at publish time, or
///     <see langword="null" /> for source snapshots without a concurrency token.
/// </param>
public sealed record EventOutboxMessage(
    string  EventType,
    string? Payload,
    string? CorrelationId,
    string? SourceType    = null,
    string? Source = null,
    Guid?   SourceTimestamp     = null);
