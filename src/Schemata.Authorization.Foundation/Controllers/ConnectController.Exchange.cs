// Some code is borrowed from OpenIddict
// https://github.com/openiddict/openiddict-core/blob/b32eb8c0a29c9e1cccb15e5d2ac2f6d4f8b7243b/sandbox/OpenIddict.Sandbox.AspNetCore.Server/Controllers/AuthorizationController.cs
// https://github.com/openiddict/openiddict-samples/blob/e5adebcb551975fe2a1adeacf940b13b42d27eff/samples/Aridka/Aridka.Server/Controllers/AuthorizationController.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Schemata.Authorization.Foundation.Controllers;

public sealed partial class ConnectController : ControllerBase
{
    [HttpPost(nameof(Token))]
    public async Task<IActionResult> Token() {
        var request = HttpContext.GetOpenIddictServerRequest()
                   ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType()
         || request.IsDeviceCodeGrantType()
         || request.IsRefreshTokenGrantType()) {
            // Retrieve the claims principal stored in the authorization code/device code/refresh token.
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // Retrieve the user profile corresponding to the authorization code/refresh token.
            var user = await _users.GetUserAsync(result.Principal!);
            if (user is null) {
                return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "The token is no longer valid.",
                    }));
            }

            // Ensure the user is still allowed to sign in.
            if (!await _sign.CanSignInAsync(user)) {
                return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = OpenIddictConstants.Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "The user is no longer allowed to sign in.",
                    }));
            }

            var identity = new ClaimsIdentity(result.Principal?.Claims,
                TokenValidationParameters.DefaultAuthenticationType, OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);

            // Override the user claims present in the principal in case they
            // changed since the authorization code/refresh token was issued.
            identity.SetClaim(OpenIddictConstants.Claims.Subject, await _users.GetUserIdAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.Email, await _users.GetEmailAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.Name, await _users.GetUserNameAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.PreferredUsername, await _users.GetUserNameAsync(user))
                    .SetClaims(OpenIddictConstants.Claims.Role, (await _users.GetRolesAsync(user)).ToImmutableArray());

            identity.SetDestinations(GetDestinations);

            // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
            return SignIn(new(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType()) {
            // Note: the client credentials are automatically validated by OpenIddict:
            // if client_id or client_secret are invalid, this action won't be invoked.

            var application = await _applications.FindByClientIdAsync(request.ClientId!);
            if (application == null) {
                throw new InvalidOperationException("The application details cannot be found in the database.");
            }

            // Create the claims-based identity that will be used by OpenIddict to generate tokens.
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);

            // Add the claims that will be persisted in the tokens (use the client_id as the subject identifier).
            identity.SetClaim(OpenIddictConstants.Claims.Subject, await _applications.GetClientIdAsync(application));
            identity.SetClaim(OpenIddictConstants.Claims.Name, await _applications.GetDisplayNameAsync(application));

            // Note: In the original OAuth 2.0 specification, the client credentials grant
            // doesn't return an identity token, which is an OpenID Connect concept.
            //
            // As a non-standardized extension, OpenIddict allows returning an id_token
            // to convey information about the client application when the "openid" scope
            // is granted (i.e specified when calling principal.SetScopes()). When the "openid"
            // scope is not explicitly set, no identity token is returned to the client application.

            // Set the list of scopes granted to the client application in access_token.
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await _scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            return SignIn(new(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }
}
