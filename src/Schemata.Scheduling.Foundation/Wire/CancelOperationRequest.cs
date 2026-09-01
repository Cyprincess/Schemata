using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Identifies one long-running operation to cancel.
/// </summary>
public sealed class CancelOperationRequest : ICanonicalName, ICommand<Operation>, IRequestPrincipal
{
    /// <summary>
    ///     Canonical name of the operation to cancel.
    /// </summary>
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
