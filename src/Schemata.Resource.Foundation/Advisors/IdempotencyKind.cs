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