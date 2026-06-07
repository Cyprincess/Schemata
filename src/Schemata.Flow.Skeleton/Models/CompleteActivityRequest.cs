using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for completing the current activity of a process instance.</summary>
public sealed class CompleteActivityRequest : ICanonicalName
{
    /// <summary>Serialized variables merged into the process instance.</summary>
    public string? Variables { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
