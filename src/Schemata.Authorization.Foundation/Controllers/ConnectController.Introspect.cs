using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpPost("Introspect")]
    public async Task<IActionResult> Introspect([FromForm] IntrospectRequest request, CancellationToken ct) {
        var headers = CollectHeaders();
        var result  = await dispatcher.SendAsync<IntrospectionEndpointQuery, IntrospectionResponse>(
            new(request, headers), ct);
        return Ok(result);
    }
}
