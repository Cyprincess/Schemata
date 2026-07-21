namespace Schemata.Resource.Foundation;

/// <summary>
///     Persisted arguments for a durable AIP-165 purge operation. Carries the original
///     request data so the operation can be rebuilt and run after a host restart; execution
///     recompiles the filter from the saved expression.
/// </summary>
public sealed class PurgeOperationArgs
{
    /// <summary>AIP filter expression selecting resources to purge; <c>*</c> selects all.</summary>
    public string? Filter { get; init; }

    /// <summary>The filter expression language; defaults to the resource's first enabled language.</summary>
    public string? Language { get; init; }

    /// <summary>Parent resource name narrowing the purge to a child collection.</summary>
    public string? Parent { get; init; }

    /// <summary>When <see langword="true" />, physically removes matches; <see langword="false" /> previews.</summary>
    public bool Force { get; init; }
}
