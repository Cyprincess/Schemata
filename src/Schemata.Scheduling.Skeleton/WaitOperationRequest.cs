using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Entities;

namespace Schemata.Scheduling.Skeleton;

/// <summary>Request body for the <c>:wait</c> custom method on an operation resource.</summary>
public sealed class WaitOperationRequest : ICanonicalName, IQuery<Operation>, IRequestPrincipal
{
    /// <summary>Maximum server-side wait duration requested by the caller.</summary>
    public TimeSpan? Timeout { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    #endregion
}
