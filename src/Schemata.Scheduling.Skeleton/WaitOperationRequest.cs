using System;
using System.ComponentModel;
using Schemata.Abstractions.Entities;

namespace Schemata.Scheduling.Skeleton;

[DisplayName("WaitOperationRequest")]
[CanonicalName("operations/{operation}")]
public sealed class WaitOperationRequest : ICanonicalName
{
    public TimeSpan? Timeout { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
