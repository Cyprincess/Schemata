using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

using Schemata.Abstractions.Advisors;
namespace Schemata.Authorization.Foundation.Commands;

public sealed record CheckSessionEndpointRequest : ICommand<string>, IEndpointRequest<SessionManagementEndpoint, string>
{
    public Task<string> ExecuteAsync(SessionManagementEndpoint endpoint, AdviceContext context, CancellationToken ct) {
        return endpoint.CheckSessionAsync(ct);
    }
}