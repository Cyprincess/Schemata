using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record RevokeEndpointRequest(
    RevokeRequest                      Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}