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

public sealed record DeviceAuthorizeEndpointRequest(
    DeviceAuthorizeRequest             Request,
    Dictionary<string, List<string?>>? Headers
) : ICommand<AuthorizationResult>, IEndpointRequest<DeviceAuthorizeEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; }

    public Task<AuthorizationResult> ExecuteAsync(DeviceAuthorizeEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.DeviceAuthorizeAsync(Request, Headers, context, ct);
    }
}
