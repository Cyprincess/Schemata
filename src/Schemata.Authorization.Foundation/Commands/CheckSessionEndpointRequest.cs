using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Commands;

public sealed record CheckSessionEndpointRequest : ICommand<string>, IEndpointRequest<SessionManagementEndpoint, string>
{
    public Task<string> ExecuteAsync(SessionManagementEndpoint endpoint, CancellationToken ct) {
        return endpoint.CheckSessionAsync(ct);
    }
}