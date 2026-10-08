using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterReadQuery(string? ClientId, string? BearerToken)
    : IQuery<RegistrationResponse?>, IEndpointRequest<RegisterEndpoint, RegistrationResponse?>
{
    public Task<RegistrationResponse?> ExecuteAsync(RegisterEndpoint endpoint, CancellationToken ct) {
        return endpoint.ReadAsync(ClientId, BearerToken, ct);
    }
}
