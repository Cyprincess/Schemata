using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterDeleteQuery(string? ClientId, string? BearerToken)
    : IQuery<bool>, IEndpointRequest<RegisterEndpoint, bool>
{
    public Task<bool> ExecuteAsync(RegisterEndpoint endpoint, CancellationToken ct) {
        return endpoint.DeleteAsync(ClientId, BearerToken, ct);
    }
}
