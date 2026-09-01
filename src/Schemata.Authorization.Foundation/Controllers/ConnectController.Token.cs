using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Token")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request, CancellationToken ct) {
        var headers = CollectHeaders();
        var result  = await dispatcher.SendAsync<TokenEndpointRequest, AuthorizationResult>(
            new(request, headers), ct);
        return await MapResult(result, ct);
    }
}