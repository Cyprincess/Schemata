using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for correlating a message to a process instance.</summary>
public sealed class CorrelateMessageRequest : ICanonicalName
{
    /// <summary>The <see cref="Models.Message.Name" /> of the message definition to correlate.</summary>
    public string MessageName { get; set; } = null!;

    /// <summary>Optional serialized payload merged into process variables.</summary>
    public string? Payload { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
