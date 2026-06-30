using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for completing the current activity of a process instance.</summary>
public sealed class CompleteActivityRequest : ICanonicalName
{
    /// <summary>
    ///     Optional full canonical name of the token to advance. Required under the BPMN engine
    ///     when the process has more than one ready token; optional under the state-machine engine.
    /// </summary>
    public string? Token { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
