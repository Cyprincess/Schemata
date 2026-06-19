namespace Schemata.Resource.Foundation.Advisors;

/// <summary>The finalized (DONE) idempotency value: the persisted response and its payload hash.</summary>
internal sealed class IdempotencyEnvelope<TPayload>
{
    /// <summary>
    ///     Identifies the cached entry as a finalized idempotency response.
    /// </summary>
    public string Kind { get; set; } = IdempotencyKind.Done;

    /// <summary>
    ///     The payload hash from the original request.
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>
    ///     The response payload returned for matching replays.
    /// </summary>
    public TPayload? Payload { get; set; }
}
