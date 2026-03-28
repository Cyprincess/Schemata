using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("EndSession")]
    public Task<IActionResult> EndSessionGet([FromQuery] EndSessionRequest request, CancellationToken ct) {
        return HandleEndSession(request, ct);
    }

    [HttpPost("EndSession")]
    public Task<IActionResult> EndSessionPost([FromForm] EndSessionRequest request, CancellationToken ct) {
        return HandleEndSession(request, ct);
    }

    private async Task<IActionResult> HandleEndSession(EndSessionRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<EndSessionEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var result = await handler.HandleAsync(request, HttpContext.User, ct);

        return result.Status switch {
            AuthorizationStatus.Redirect when !string.IsNullOrWhiteSpace(result.RedirectUri) => Redirect(result.RedirectUri),
            AuthorizationStatus.Content when result.Data is string html => new ContentResult {
                Content     = html,
                ContentType = MediaTypeNames.Text.Html,
                StatusCode  = 200,
            },
            var _ => NoContent(),
        };
    }
}
