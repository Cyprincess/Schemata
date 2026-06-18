using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Metadata describing an <see cref="Operation" />: the method that produced it
///     and its execution timing, per the AIP-151 <c>metadata</c> field.
/// </summary>
public sealed class OperationMetadata
{
    /// <summary>The custom method verb that produced this operation (e.g. <c>purge</c>).</summary>
    public string? Method { get; set; }

    /// <summary>Canonical name of the originating job.</summary>
    public string? Job { get; set; }

    /// <summary>Wall-clock start time.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Wall-clock end time, set when the operation finishes.</summary>
    public DateTime? EndTime { get; set; }
}
