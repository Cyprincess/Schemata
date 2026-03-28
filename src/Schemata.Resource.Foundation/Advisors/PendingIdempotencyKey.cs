namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Tracks a pending idempotency key to be stored after the create operation completes.
/// </summary>
/// <param name="RequestId">The client-supplied request identifier.</param>
internal sealed record PendingIdempotencyKey(string RequestId);
