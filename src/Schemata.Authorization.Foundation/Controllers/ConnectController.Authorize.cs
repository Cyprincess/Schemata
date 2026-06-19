using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

/// <summary>
///     Contains authorization endpoint actions for GET and POST requests.
/// </summary>
public partial class ConnectController
{
    /// <summary>Handles authorization requests submitted through the query string.</summary>
    [HttpGet("Authorize")]
    public Task<IActionResult> AuthorizeGet([FromQuery] AuthorizeRequest request, CancellationToken ct) {
        return HandleAuthorize(request, ct);
    }

    /// <summary>Handles authorization requests submitted as form posts.</summary>
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
