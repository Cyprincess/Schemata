using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Handlers;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("Profile")]
    [HttpPost("Profile")]
    [Authorize(AuthenticationSchemes = SchemataAuthorizationSchemes.Bearer)]
    public async Task<IActionResult> Profile(CancellationToken ct) {
        var handler = HttpContext.RequestServices.GetService<UserInfoEndpoint>();
        if (handler is null) {
            throw new NotFoundException();
        }

        var result = await handler.HandleAsync(HttpContext.User, ct);
        return MapResult(result);
    }
}
