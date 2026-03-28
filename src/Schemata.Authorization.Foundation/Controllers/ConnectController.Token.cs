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
    [HttpPost("Token")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request, CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<TokenEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var headers = CollectHeaders();
        var result  = await handler.HandleAsync(request, headers, ct);
        return MapResult(result);
    }
}
