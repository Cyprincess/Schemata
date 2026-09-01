using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Device")]
    public async Task<IActionResult> Device([FromForm] DeviceAuthorizeRequest request, CancellationToken ct) {
        var headers = CollectHeaders();
        var result  = await dispatcher.SendAsync<DeviceAuthorizeEndpointRequest, AuthorizationResult>(
            new(request, headers), ct);
        return await MapResult(result, ct);
    }
}