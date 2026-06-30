using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for broadcasting a signal to all waiting process instances.</summary>
public sealed class ThrowSignalRequest : ICanonicalName, IRequestIdentification
{
    /// <summary>The <see cref="Models.Signal.Name" /> of the signal definition to throw.</summary>
    public string SignalName { get; set; } = null!;

    /// <summary>Optional serialized signal payload.</summary>
    public string? Payload { get; set; }

    /// <summary>
    ///     Optional full canonical name of a single token to scope the broadcast to. When
    ///     <see langword="null" />, the signal fans out to every waiting token on every matched
    ///     process (the default broadcast semantics).
    /// </summary>
    public string? Token { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    #region IRequestIdentification Members

    public string? RequestId { get; set; }

    #endregion
}
