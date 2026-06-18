using System;

namespace Schemata.Event.Skeleton;

/// <summary>
///     A persisted event replayed to the broker by the outbox dispatcher. Carries only the
///     already-serialized payload, routing metadata, and the optional optimistic-snapshot
///     reference to the originating business entity so re-publishing does not re-run the
///     publish pipeline or write another audit row.
/// </summary>
/// <param name="EventType">Wire-format event type, doubling as the routing key.</param>
/// <param name="Payload">Serialized event body.</param>
/// <param name="CorrelationId">Correlation identifier carried end-to-end.</param>
/// <param name="SourceType">
///     CLR full name of the business entity that produced the event, or <see langword="null" />
///     when the event has no semantic source.
/// </param>
/// <param name="Source">
///     Canonical resource name of the source business entity, or <see langword="null" />.
/// </param>
/// <param name="SourceTimestamp">
///     Concurrency timestamp captured from the source business entity at publish time, or
///     <see langword="null" /> when the source does not implement <c>IConcurrency</c>.
/// </param>
public sealed record EventOutboxMessage(
    string  EventType,
    string? Payload,
    string? CorrelationId,
    string? SourceType    = null,
    string? Source = null,
    Guid?   SourceTimestamp     = null);
