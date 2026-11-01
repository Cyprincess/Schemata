using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
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
        var result = await dispatcher.SendAsync<EndSessionEndpointRequest, AuthorizationResult>(
            new(request, HttpContext.User), ct);

        // The built-in logout completion reports an empty successful outcome (content with no
        // data) when neither a post-logout redirect nor a front-channel page applies; the
        // shared adapter's content branch would otherwise serialize that null.
        if (result.Status == AuthorizationStatus.Content && result.Data is null) {
            return NoContent();
        }

        return await MapResult(result, ct);
    }
}
