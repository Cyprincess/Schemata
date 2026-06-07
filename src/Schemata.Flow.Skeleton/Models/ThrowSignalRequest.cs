using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for broadcasting a signal to all waiting process instances.</summary>
public sealed class ThrowSignalRequest : IRequestIdentification
{
    /// <summary>The <see cref="Models.Signal.Name" /> of the signal definition to throw.</summary>
    public string SignalName { get; set; } = null!;

    /// <summary>Optional serialized payload merged into matched process variables.</summary>
    public string? Payload { get; set; }

    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
