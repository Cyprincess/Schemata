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
    [HttpGet("Interact")]
    public async Task<IActionResult> Interact([FromQuery] InteractRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<InteractionEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var issuer = options.Value.Issuer!;

        var result = await handler.GetDetailsAsync(request, issuer, ct);
        return MapResult(result);
    }

    [HttpPost("Interact")]
    public async Task<IActionResult> ApproveInteraction([FromForm] InteractRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<InteractionEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var issuer = options.Value.Issuer!;

        var result = await handler.ApproveAsync(request, HttpContext.User, issuer, ct);
        return MapResult(result);
    }

    [HttpDelete("Interact")]
    public async Task<IActionResult> DenyInteraction([FromQuery] InteractRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<InteractionEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        await handler.DenyAsync(request, ct);
        throw new NoContentException();
    }
}
