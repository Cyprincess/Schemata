using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Authorization.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record UserInfoEndpointQuery(
    ClaimsPrincipal Principal
) : IQuery<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}