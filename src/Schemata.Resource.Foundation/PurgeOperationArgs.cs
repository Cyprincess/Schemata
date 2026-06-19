namespace Schemata.Resource.Foundation;

/// <summary>
///     Persisted arguments for a durable AIP-165 purge operation. Carries the original
///     request data so the operation can be rebuilt and run after a host restart; execution
///     recompiles the filter from the saved expression.
/// </summary>
public sealed class PurgeOperationArgs
{
    /// <summary>AIP filter expression selecting resources to purge; <c>*</c> selects all.</summary>
    public string? Filter { get; set; }

    /// <summary>When <see langword="true" />, physically removes matches; otherwise previews.</summary>
    public bool Force { get; set; }
}
