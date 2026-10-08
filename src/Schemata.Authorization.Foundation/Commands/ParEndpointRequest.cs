using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record ParEndpointRequest(
    AuthorizeRequest                     Request,
    Dictionary<string, List<string?>>    Form,
    Dictionary<string, List<string?>>?   Headers
) : ICommand<AuthorizationResult>, IEndpointRequest<ParEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<AuthorizationResult> ExecuteAsync(ParEndpoint endpoint, CancellationToken ct) {
        return endpoint.ParAsync(Request, Form, Headers, ct);
    }
}