using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record EndSessionEndpointRequest(
    EndSessionRequest Request,
    ClaimsPrincipal?  Principal
) : ICommand<AuthorizationResult>, IEndpointRequest<EndSessionEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;

    public Task<AuthorizationResult> ExecuteAsync(EndSessionEndpoint endpoint, CancellationToken ct) {
        return Principal is null
            ? Task.FromResult(AuthorizationResult.Challenge())
            : endpoint.HandleAsync(Request, Principal, ct);
    }
}
