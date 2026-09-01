using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record InteractionDenyRequest(
    InteractRequest Request
) : ICommand<Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}