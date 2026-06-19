using System;
using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Request body for the <c>:wait</c> custom method on an operation resource.</summary>
[DisplayName("WaitOperationRequest")]
[CanonicalName("operations/{operation}")]
public sealed class WaitOperationRequest : ICanonicalName
{
    /// <summary>Maximum server-side wait duration requested by the caller.</summary>
    public TimeSpan? Timeout { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
