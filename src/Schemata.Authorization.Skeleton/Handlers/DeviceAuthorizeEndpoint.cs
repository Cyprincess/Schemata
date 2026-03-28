using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

public abstract class DeviceAuthorizeEndpoint
{
    public abstract Task<AuthorizationResult> DeviceAuthorizeAsync(
        DeviceAuthorizeRequest             request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
