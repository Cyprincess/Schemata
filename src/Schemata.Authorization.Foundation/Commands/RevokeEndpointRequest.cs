using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record RevokeEndpointRequest(
    RevokeRequest                      Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<Unit>, IEndpointRequest<RevocationEndpoint, Unit>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public async Task<Unit> ExecuteAsync(RevocationEndpoint endpoint, CancellationToken ct) {
        await endpoint.HandleAsync(Request, Headers, ct);
        return Unit.Value;
    }
}
