using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record InteractionDetailsQuery(
    InteractRequest Request,
    string          Issuer
) : IQuery<AuthorizationResult>, IEndpointRequest<InteractionEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<AuthorizationResult> ExecuteAsync(InteractionEndpoint endpoint, CancellationToken ct) {
        return endpoint.GetDetailsAsync(Request, Issuer, ct);
    }
}
