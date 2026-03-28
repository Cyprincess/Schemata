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
    [HttpPost("Introspect")]
    public async Task<IActionResult> Introspect([FromForm] IntrospectRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<IntrospectionEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var headers = CollectHeaders();
        var result  = await handler.HandleAsync(request, headers, ct);
        return Ok(result);
    }
}
