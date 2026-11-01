using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

using Schemata.Abstractions.Advisors;
namespace Schemata.Authorization.Foundation.Queries;

public sealed record IntrospectionEndpointQuery(
    IntrospectRequest                  Request,
    Dictionary<string, List<string?>>? Headers
) : IQuery<IntrospectionResponse>, IEndpointRequest<IntrospectionEndpoint, IntrospectionResponse>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<IntrospectionResponse> ExecuteAsync(IntrospectionEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.HandleAsync(Request, Headers, context, ct);
    }
}
