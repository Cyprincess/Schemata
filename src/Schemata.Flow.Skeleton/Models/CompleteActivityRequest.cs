using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>Request body for completing the current activity of a process instance.</summary>
public sealed class CompleteActivityRequest : ICanonicalName, ICommand<ProcessSnapshot>, IRequestPrincipal
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

    #region IRequestPrincipal Members

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    #endregion
}
