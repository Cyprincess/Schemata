using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

/// <summary>
///     Contains the token endpoint action.
/// </summary>
public partial class ConnectController
{
    /// <summary>Handles token endpoint requests.</summary>
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
