using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Runtime;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>
    ///     Resumes the request that triggered a sign-in redirect. The <c>continue</c> payload is
    ///     unprotected and rejected unless it decodes to a local path, so a forged or replayed value
    ///     cannot turn this endpoint into an open redirect.
    /// </summary>
    /// <param name="token">The protected continuation payload issued with the login redirect.</param>
    /// <param name="protection">The Data Protection provider that issued the payload.</param>
    [HttpGet(nameof(Continue))]
    public IActionResult Continue(
        [FromQuery(Name = LoginContinuation.Parameter)] string? token,
        [FromServices]                                 IDataProtectionProvider protection
    ) {
        string target;
        try {
            target = protection.CreateProtector(LoginContinuation.Purpose).Unprotect(token ?? string.Empty);
        } catch (CryptographicException) {
            throw Rejected();
        }

        if (!Url.IsLocalUrl(target)) {
            throw Rejected();
        }

        return Redirect(target);
    }

    private static ValidationException Rejected() {
        return new([
            new() {
                Field       = LoginContinuation.Parameter,
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAYLOAD),
                Reason      = SchemataResources.INVALID_PAYLOAD,
            },
        ]);
    }
}
