using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    [HttpPost(nameof(Forgot))]
    public async Task<IActionResult> Forgot([FromBody] ForgetRequest request, CancellationToken ct) {
        var result = await handler.ForgotAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [HttpPost(nameof(Reset))]
    public async Task<IActionResult> Reset([FromBody] ResetRequest request, CancellationToken ct) {
        var result = await handler.ResetAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }
}
