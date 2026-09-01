namespace Schemata.Resource.Foundation.Advisors;

/// <summary>Reads only the <see cref="Kind" /> discriminator so a cached value can be classified before its full shape is known.</summary>
internal sealed class IdempotencyHeader
{
    /// <summary>
    ///     The cached idempotency entry discriminator.
    /// </summary>
    public string? Kind { get; set; }
}