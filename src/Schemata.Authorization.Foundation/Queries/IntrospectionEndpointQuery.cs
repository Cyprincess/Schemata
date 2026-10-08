using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record IntrospectionEndpointQuery(
    IntrospectRequest                  Request,
    Dictionary<string, List<string?>>? Headers
) : IQuery<IntrospectionResponse>, IEndpointRequest<IntrospectionEndpoint, IntrospectionResponse>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<IntrospectionResponse> ExecuteAsync(IntrospectionEndpoint endpoint, CancellationToken ct) {
        return endpoint.HandleAsync(Request, Headers, ct);
    }
}
