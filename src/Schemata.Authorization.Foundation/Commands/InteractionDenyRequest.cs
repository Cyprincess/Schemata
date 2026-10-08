using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record InteractionDenyRequest(
    InteractRequest Request
) : ICommand<Unit>, IEndpointRequest<InteractionEndpoint, Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public async Task<Unit> ExecuteAsync(InteractionEndpoint endpoint, CancellationToken ct) {
        await endpoint.DenyAsync(Request, ct);
        return Unit.Value;
    }
}
