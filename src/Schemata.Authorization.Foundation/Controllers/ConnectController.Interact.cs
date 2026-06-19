using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

/// <summary>
///     Contains user interaction endpoint actions for device and authorization flows.
/// </summary>
public partial class ConnectController
{
    /// <summary>Returns interaction details for a pending user-code request.</summary>
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

    /// <summary>Approves a pending interaction request for the authenticated user.</summary>
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

    /// <summary>Denies a pending interaction request.</summary>
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
