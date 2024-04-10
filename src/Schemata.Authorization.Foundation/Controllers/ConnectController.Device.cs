// Some code is borrowed from OpenIddict
// https://github.com/openiddict/openiddict-core/blob/b32eb8c0a29c9e1cccb15e5d2ac2f6d4f8b7243b/sandbox/OpenIddict.Sandbox.AspNetCore.Server/Controllers/AuthorizationController.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public sealed partial class ConnectController : ControllerBase
{
    [Authorize]
    [HttpGet(nameof(Verify))]
    public async Task<IActionResult> Verify() {
        // Retrieve the claims principal associated with the user code.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Succeeded && !string.IsNullOrEmpty(result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId))) {
            // Retrieve the application details from the database using the client_id stored in the principal.
            var application
                = await _applications.FindByClientIdAsync(
                      result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId)!)
               ?? throw new InvalidOperationException(
                      "Details concerning the calling client application cannot be found.");

            // Render a form asking the user to confirm the authorization demand.
            var response = new VerifyResponse {
                ApplicationName = await _applications.GetLocalizedDisplayNameAsync(application),
                UserCode        = result.Properties.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode),
                Scopes          = [],
            };

            var scopes = _scopes.FindByNamesAsync(result.Principal.GetScopes());
            await foreach (var scope in scopes) {
                response.Scopes.Add(new() {
                    Name        = await _scopes.GetNameAsync(scope),
                    DisplayName = await _scopes.GetLocalizedDisplayNameAsync(scope),
                    Description = await _scopes.GetLocalizedDescriptionAsync(scope),
                });
            }

            return Ok(response);
        }

        // If a user code was specified (e.g as part of the verification_uri_complete)
        // but is not valid, render a form asking the user to enter the user code manually.
        if (!string.IsNullOrEmpty(result.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode))) {
            return BadRequest(new ErrorResponse {
                Error            = OpenIddictConstants.Errors.InvalidToken,
                ErrorDescription = "The specified user code is not valid. Please make sure you typed it correctly.",
            });
        }

        // Otherwise, render a form asking the user to enter the user code manually.
        return Ok(new VerifyResponse());
    }

    [Authorize]
    [HttpPost(nameof(Verify))]
    public async Task<IActionResult> VerifyAccept() {
        // Retrieve the profile of the logged in user.
        var user = await _users.GetUserAsync(User)
                ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the claims principal associated with the user code.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Succeeded && !string.IsNullOrEmpty(result.Principal.GetClaim(OpenIddictConstants.Claims.ClientId))) {
            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Subject, OpenIddictConstants.Claims.Role);

            // Add the claims that will be persisted in the tokens.
            identity.SetClaim(OpenIddictConstants.Claims.Subject, await _users.GetUserIdAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.Email, await _users.GetEmailAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.PhoneNumber, await _users.GetPhoneNumberAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.PreferredUsername, await _users.GetUserNameAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.Nickname, await _users.GetDisplayNameAsync(user))
                    .SetClaims(OpenIddictConstants.Claims.Role, (await _users.GetRolesAsync(user)).ToImmutableArray());

            // Note: in this sample, the granted scopes match the requested scope
            // but you may want to allow the user to uncheck specific scopes.
            // For that, simply restrict the list of scopes before calling SetScopes.
            identity.SetScopes(result.Principal.GetScopes());
            identity.SetResources(await _scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            var properties = new AuthenticationProperties {
                // This property points to the address OpenIddict will automatically
                // redirect the user to after validating the authorization demand.
                RedirectUri = "/",
            };

            return SignIn(new(identity), properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // Redisplay the form when the user code is not valid.
        return BadRequest(new ErrorResponse {
            Error            = OpenIddictConstants.Errors.InvalidToken,
            ErrorDescription = "The specified user code is not valid. Please make sure you typed it correctly.",
        });
    }
}
