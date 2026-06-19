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
    /// <summary>Confirms an email address or phone number with a confirmation code.</summary>
    [HttpGet(nameof(Confirm))]
    public async Task<IActionResult> Confirm([FromQuery] ConfirmRequest request, CancellationToken ct) {
        var result = await handler.ConfirmAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Sends an account-confirmation code to a contact address.</summary>
    [HttpPost(nameof(Code))]
    public async Task<IActionResult> Code([FromBody] ForgetRequest request, CancellationToken ct) {
        var result = await handler.CodeAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }
}
