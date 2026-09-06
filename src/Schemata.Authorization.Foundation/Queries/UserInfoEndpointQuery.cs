using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record UserInfoEndpointQuery(
    ClaimsPrincipal Principal
) : IQuery<AuthorizationResult>, IEndpointRequest<UserInfoEndpoint, AuthorizationResult>, IRequestPrincipal
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;

    public Task<AuthorizationResult> ExecuteAsync(UserInfoEndpoint endpoint, CancellationToken ct) {
        return Principal is null
            ? Task.FromResult(AuthorizationResult.Challenge())
            : endpoint.HandleAsync(Principal, ct);
    }
}
