using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for broadcasting a signal to all waiting process instances.</summary>
public sealed class ThrowSignalRequest : ICanonicalName, IRequestIdentification
{
    /// <summary>The <see cref="Models.Signal.Name" /> of the signal definition to throw.</summary>
    public string SignalName { get; set; } = null!;

    /// <summary>Optional serialized payload merged into matched process variables.</summary>
    public string? Payload { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
