using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
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