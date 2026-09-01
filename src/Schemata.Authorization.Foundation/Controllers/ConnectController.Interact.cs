using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("Interact")]
    public async Task<IActionResult> Interact([FromQuery] InteractRequest request, CancellationToken ct) {
        var issuer = options.Value.Issuer!;
        var result = await dispatcher.SendAsync<InteractionDetailsQuery, AuthorizationResult>(
            new(request, issuer), ct);
        return await MapResult(result, ct);
    }

    [HttpPost("Interact")]
    public async Task<IActionResult> ApproveInteraction([FromForm] InteractRequest request, CancellationToken ct) {
        var issuer = options.Value.Issuer!;
        var result = await dispatcher.SendAsync<InteractionApproveRequest, AuthorizationResult>(
            new(request, HttpContext.User, issuer), ct);
        return await MapResult(result, ct);
    }

    [HttpDelete("Interact")]
    public async Task<IActionResult> DenyInteraction([FromQuery] InteractRequest request, CancellationToken ct) {
        await dispatcher.SendAsync<InteractionDenyRequest, Unit>(new(request), ct);
        throw new NoContentException();
    }
}