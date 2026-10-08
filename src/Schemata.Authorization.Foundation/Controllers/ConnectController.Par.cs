using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Par")]
    public async Task<IActionResult> Par([FromForm] AuthorizeRequest request, CancellationToken ct) {
        Response.Headers.CacheControl = "no-cache, no-store";
        var headers = CollectHeaders();
        var form = (await HttpContext.Request.ReadFormAsync(ct))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Select(value => (string?)value).ToList());
        var result = await dispatcher.SendAsync<ParEndpointRequest, AuthorizationResult>(
            new(request, form, headers), ct);

        if (result.Status == AuthorizationStatus.Content && result.Data is PushedAuthorizationResponse response) {
            return new ObjectResult(response) { StatusCode = StatusCodes.Status201Created };
        }

        return await MapResult(result, ct);
    }
}