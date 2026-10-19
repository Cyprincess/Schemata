using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Queries;
using AuthorizationResult = Schemata.Authorization.Skeleton.AuthorizationResult;

namespace Schemata.Authorization.Foundation.Controllers;

public partial class ConnectController
{
    [HttpGet("Profile")]
    [HttpPost("Profile")]
    [Authorize(Policy = SchemataAuthorizationPolicies.Profile)]
    public async Task<IActionResult> Profile(CancellationToken ct) {
        var result = await dispatcher.SendAsync<UserInfoEndpointQuery, AuthorizationResult>(
            new(HttpContext.User), ct);
        return await MapResult(result, ct);
    }
}