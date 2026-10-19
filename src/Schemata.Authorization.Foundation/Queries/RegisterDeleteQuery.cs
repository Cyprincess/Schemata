using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

using Schemata.Abstractions.Advisors;
namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterDeleteQuery(string? ClientId, string? BearerToken)
    : IQuery<bool>, IEndpointRequest<RegisterEndpoint, bool>
{
    public Task<bool> ExecuteAsync(RegisterEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.DeleteAsync(ClientId, BearerToken, context, ct);
    }
}
