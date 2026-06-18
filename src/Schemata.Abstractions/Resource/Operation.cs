using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Long-running operation envelope per
///     <seealso href="https://google.aip.dev/151">AIP-151: Long-running operations</seealso>.
///     Returned by custom methods that dispatch background work (e.g. <c>:run</c>,
///     <c>:purge</c>) and by reads on the <c>operations/{operation}</c> resource.
/// </summary>
[DisplayName("Operation")]
[CanonicalName("operations/{operation}")]
public sealed class Operation : ICanonicalName
{
    /// <summary>Whether the operation has reached a terminal state.</summary>
    public bool Done { get; set; }

    /// <summary>Error status set when the operation finished unsuccessfully.</summary>
    public OperationStatus? Error { get; set; }

    /// <summary>Result payload set when the operation finished successfully.</summary>
    public OperationResponse? Response { get; set; }

    /// <summary>Operation metadata describing the originating method and timing.</summary>
    public OperationMetadata? Metadata { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
