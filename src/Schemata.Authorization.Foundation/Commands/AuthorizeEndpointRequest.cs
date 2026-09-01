using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record AuthorizeEndpointRequest(
    AuthorizeRequest                 Request,
    ClaimsPrincipal?                 Principal
) : ICommand<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}