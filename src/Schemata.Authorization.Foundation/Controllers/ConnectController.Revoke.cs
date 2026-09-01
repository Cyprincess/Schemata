using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Revoke")]
    public async Task<IActionResult> Revoke([FromForm] RevokeRequest request, CancellationToken ct) {
        var headers = CollectHeaders();
        await dispatcher.SendAsync<RevokeEndpointRequest, Unit>(new(request, headers), ct);
        return Ok();
    }
}