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
    [HttpGet("Authorize")]
    public Task<IActionResult> AuthorizeGet([FromQuery] AuthorizeRequest request, CancellationToken ct) {
        return HandleAuthorize(request, ct);
    }

    [HttpPost("Authorize")]
    public Task<IActionResult> AuthorizePost([FromForm] AuthorizeRequest request, CancellationToken ct) {
        return HandleAuthorize(request, ct);
    }

    private async Task<IActionResult> HandleAuthorize(AuthorizeRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<AuthorizeEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var result = await handler.AuthorizeAsync(request, HttpContext.User, ct);
        return MapResult(result);
    }
}
