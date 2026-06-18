using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>Discriminator values distinguishing a reserved PENDING entry from a finalized DONE envelope.</summary>
internal static class IdempotencyKind
{
    public const string Pending = "PENDING";
    public const string Done    = "DONE";
}

/// <summary>Reads only the <see cref="Kind" /> discriminator so a cached value can be classified before its full shape is known.</summary>
internal sealed class IdempotencyHeader
{
    public string? Kind { get; set; }
}

/// <summary>
///     Stashed in the <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> by a request
///     idempotency advisor after it reserves the cache key, so the response advisor can swap the
///     exact reserved <see cref="PendingBytes" /> for the finalized envelope via compare-and-swap.
/// </summary>
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
    public string Kind { get; set; } = IdempotencyKind.Pending;

    public string OwnerToken { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string Principal { get; set; } = string.Empty;

    public string CanonicalName { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }
}
