using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record IntrospectionEndpointQuery(
    IntrospectRequest                  Request,
    Dictionary<string, List<string?>>? Headers
) : IQuery<IntrospectionResponse>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed record UserInfoEndpointQuery(
    ClaimsPrincipal Principal
) : IQuery<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}

public sealed record InteractionDetailsQuery(
    InteractRequest Request,
    string          Issuer
) : IQuery<AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }
}
