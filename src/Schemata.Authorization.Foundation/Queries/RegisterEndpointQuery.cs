using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

using Schemata.Abstractions.Advisors;
namespace Schemata.Authorization.Foundation.Queries;

public sealed record RegisterEndpointQuery(RegisterRequest Request, string? BearerToken)
    : IQuery<RegistrationResponse>, IEndpointRequest<RegisterEndpoint, RegistrationResponse>
{
    public Task<RegistrationResponse> ExecuteAsync(RegisterEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.HandleAsync(Request, BearerToken, context, ct);
    }
}
