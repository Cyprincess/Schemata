using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Handlers;

public abstract class SessionManagementEndpoint
{
    public abstract Task<string> CheckSessionAsync(CancellationToken ct);
}