using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("Authorize")]
    public Task<IActionResult> AuthorizeGet([FromQuery] AuthorizeRequest request, CancellationToken ct) {
        return HandleAuthorize(request, ct);
    }

    [HttpPost("Authorize")]
    public Task<IActionResult> AuthorizePost([FromForm] AuthorizeRequest request, CancellationToken ct) {
        return HandleAuthorize(request, ct);
    }

    private async Task<IActionResult> HandleAuthorize(AuthorizeRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<AuthorizeEndpointRequest, AuthorizationResult>(
            new(request, HttpContext.User), ct);
        return await MapResult(result, ct);
    }
}