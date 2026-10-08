using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Models;

namespace Schemata.Push.Http.Controllers;

[ApiController]
[Authorize]
[Route("~/v1/push")]
public sealed class PushController(IRequestDispatcher dispatcher) : ControllerBase
{
    [HttpPost("subscriptions")]
    [Authorize(Policy = PushPolicies.Create)]
    public async Task<IActionResult> Create([FromBody] CreatePushSubscriptionRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<CreatePushControlRequest, PushSubscriptionInfo>(
            new(User, request.Provider, request.ProviderKey, request.Metadata), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("subscriptions")]
    [Authorize(Policy = PushPolicies.List)]
    public async Task<IActionResult> List([FromQuery] string? provider, CancellationToken ct) {
        var subscriptions = await dispatcher.SendAsync<ListPushControlRequest, IReadOnlyList<PushSubscriptionInfo>>(
            new(User, provider), ct);
        return Ok(new { subscriptions });
    }

    [HttpDelete("subscriptions")]
    [Authorize(Policy = PushPolicies.Delete)]
    public async Task<IActionResult> Delete([FromBody] DeletePushSubscriptionRequest request, CancellationToken ct) {
        _ = await dispatcher.SendAsync<DeletePushControlRequest, Unit>(new(User, request.Provider, request.ProviderKey), ct);
        return NoContent();
    }

    [HttpPost("subscriptions:send")]
    [Authorize(Policy = PushPolicies.Send)]
    public async Task<IActionResult> Send([FromBody] SendPushWireRequest request, CancellationToken ct) {
        var results = await dispatcher.SendAsync<SendPushControlRequest, ImmutableArray<TransportResult>>(
            new(User, request.Message, request.Target, request.Options, request.Metadata), ct);
        return Ok(new { results });
    }
}
