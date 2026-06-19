using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>Discriminator values distinguishing a reserved PENDING entry from a finalized DONE envelope.</summary>
internal static class IdempotencyKind
{
    /// <summary>
    ///     Discriminator for reserved idempotency entries.
    /// </summary>
    public const string Pending = "PENDING";

    /// <summary>
    ///     Discriminator for finalized idempotency entries.
    /// </summary>
    public const string Done    = "DONE";
}

/// <summary>Reads only the <see cref="Kind" /> discriminator so a cached value can be classified before its full shape is known.</summary>
internal sealed class IdempotencyHeader
{
    /// <summary>
    ///     The cached idempotency entry discriminator.
    /// </summary>
    public string? Kind { get; set; }
}

/// <summary>
///     Stashed in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> by a request
///     idempotency advisor after it reserves the cache key, so the response advisor can swap the
///     exact reserved <see cref="PendingBytes" /> for the finalized envelope via compare-and-swap.
/// </summary>
/// <param name="Key">The cache key reserved by the request advisor.</param>
/// <param name="PayloadHash">The request payload hash associated with the reservation.</param>
/// <param name="PendingBytes">The serialized pending value used for compare-and-swap finalization.</param>
internal sealed record IdempotencyReservation(string Key, string PayloadHash, byte[] PendingBytes);

/// <summary>
///     The reservation value written by the request idempotency advisors before an operation
///     runs, per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     <see cref="OwnerToken" /> identifies the writing attempt so the response advisor can swap
///     this exact value to a <see cref="IdempotencyEnvelope{TPayload}" /> via compare-and-swap;
///     <see cref="CanonicalName" /> and <see cref="Operation" /> let the handler recover when a
///     replay finds a still-pending entry.
/// </summary>
internal sealed class PendingIdempotencyRecord : ITimestamp
{
    /// <summary>
    ///     Identifies the cached entry as a pending reservation.
    /// </summary>
    public string Kind { get; set; } = IdempotencyKind.Pending;

    /// <summary>
    ///     Unique token for the request attempt that reserved the key.
    /// </summary>
    public string OwnerToken { get; set; } = string.Empty;

    /// <summary>
    ///     Operation token included in the idempotency key.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    ///     Client-supplied request identifier.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    ///     Caller partition included in the idempotency key.
    /// </summary>
    public string Principal { get; set; } = string.Empty;

    /// <summary>
    ///     Resource name partition included in the idempotency key.
    /// </summary>
    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>
    ///     Request payload hash that rejects conflicting replays.
    /// </summary>
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>
    ///     Reservation creation timestamp.
    /// </summary>
    public DateTime? CreateTime { get; set; }

    /// <summary>
    ///     Reservation update timestamp.
    /// </summary>
    public DateTime? UpdateTime { get; set; }
}
