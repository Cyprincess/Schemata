namespace Schemata.Resource.Foundation.Advisors;

internal sealed class IdempotencyEnvelope<TPayload>
{
    public string? Hash { get; set; }

    public TPayload? Payload { get; set; }
}
