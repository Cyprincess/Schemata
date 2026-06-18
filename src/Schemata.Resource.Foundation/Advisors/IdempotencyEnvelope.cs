namespace Schemata.Resource.Foundation.Advisors;

/// <summary>The finalized (DONE) idempotency value: the persisted response and its payload hash.</summary>
internal sealed class IdempotencyEnvelope<TPayload>
{
    public string Kind { get; set; } = IdempotencyKind.Done;

    public string? Hash { get; set; }

    public TPayload? Payload { get; set; }
}
