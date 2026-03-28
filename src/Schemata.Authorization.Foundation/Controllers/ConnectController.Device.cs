using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Device")]
    public async Task<IActionResult> Device([FromForm] DeviceAuthorizeRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<DeviceAuthorizeEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var headers = CollectHeaders();
        var result  = await handler.DeviceAuthorizeAsync(request, headers, ct);
        return MapResult(result);
    }
}
