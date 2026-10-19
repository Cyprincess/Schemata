using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

using Schemata.Abstractions.Advisors;
namespace Schemata.Authorization.Foundation.Commands;

public sealed record TokenEndpointRequest(
    TokenRequest                       Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<AuthorizationResult>, IEndpointRequest<TokenEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<AuthorizationResult> ExecuteAsync(TokenEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.HandleAsync(Request, Headers, context, ct);
    }
}
