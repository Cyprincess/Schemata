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
    [HttpPost("Revoke")]
    public async Task<IActionResult> Revoke([FromForm] RevokeRequest request, CancellationToken ct) {
        var headers = CollectHeaders();
        var handler = HttpContext.RequestServices.GetService<RevocationEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        await handler.HandleAsync(request, headers, ct);
        return Ok();
    }
}
