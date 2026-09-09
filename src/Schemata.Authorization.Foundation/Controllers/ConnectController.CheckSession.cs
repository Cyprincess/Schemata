using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Handlers;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("CheckSession")]
    public async Task<ContentResult> CheckSession(CancellationToken ct) {
        Response.Headers.CacheControl = "no-store";
        var html = await dispatcher.SendAsync<CheckSessionEndpointRequest, string>(new(), ct);
        return Content(html, "text/html");
    }
}