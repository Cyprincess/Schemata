// Some code is borrowed from OpenIddict
// https://github.com/openiddict/openiddict-core/blob/b32eb8c0a29c9e1cccb15e5d2ac2f6d4f8b7243b/sandbox/OpenIddict.Sandbox.AspNetCore.Server/Controllers/AuthorizationController.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Foundation.Controllers;

public sealed partial class ConnectController : ControllerBase
{
    [HttpGet(nameof(Authorize))]
    public async Task<IActionResult> Authorize() {
        var request = HttpContext.GetOpenIddictServerRequest()
                   ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Try to retrieve the user principal stored in the authentication cookie and redirect
        // the user agent to the login page (or to an external provider) in the following cases:
        //
        //  - If the user principal can't be extracted or the cookie is too old.
        //  - If prompt=login was specified by the client application.
        //  - If a max_age parameter was provided and the authentication cookie is not considered "fresh" enough.
        //
        // For scenarios where the default authentication handler configured in the ASP.NET Core
        // authentication options shouldn't be used, a specific scheme can be specified here.
        var result = await HttpContext.AuthenticateAsync();
        if (result is not { Succeeded: true }
         || request.HasPrompt(OpenIddictConstants.Prompts.Login)
         || (request.MaxAge is not null
          && result.Properties?.IssuedUtc is not null
          && DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value))) {
            // If the client application requested promptless authentication,
            // return an error indicating that the user is not logged in.
            if (request.HasPrompt(OpenIddictConstants.Prompts.None)) {
                return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = OpenIddictConstants.Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "The user is not logged in.",
                    }));
            }

            // To avoid endless login -> authorization redirects, the prompt=login flag
            // is removed from the authorization request payload before redirecting the user.
            var prompt = string.Join(" ", request.GetPrompts().Remove(OpenIddictConstants.Prompts.Login));

            var parameters = Request.HasFormContentType
                ? Request.Form.Where(parameter => parameter.Key != OpenIddictConstants.Parameters.Prompt).ToList()
                : Request.Query.Where(parameter => parameter.Key != OpenIddictConstants.Parameters.Prompt).ToList();

            parameters.Add(new(OpenIddictConstants.Parameters.Prompt, new(prompt)));

            /*
            // For applications that want to allow the client to select the external authentication provider
            // that will be used to authenticate the user, the identity_provider parameter can be used for that.
            if (!string.IsNullOrEmpty(request.IdentityProvider))
            {
                var registrations = await _clientService.GetClientRegistrationsAsync();
                if (!registrations.Any(registration => string.Equals(registration.ProviderName,
                    request.IdentityProvider, StringComparison.Ordinal)))
                {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new(new Dictionary<string, string>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidRequest,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "The specified identity provider is not valid."
                        }));
                }

                var properties = _sign.ConfigureExternalAuthenticationProperties(
                    provider: request.IdentityProvider,
                    redirectUrl: Url.Action("ExternalLoginCallback", "Account", new
                    {
                        ReturnUrl = Request.PathBase + Request.Path + QueryString.Create(parameters)
                    }));

                // Note: when only one client is registered in the client options,
                // specifying the issuer URI or the provider name is not required.
                properties.SetString(OpenIddictClientAspNetCoreConstants.Properties.ProviderName, request.IdentityProvider);

                // Ask the OpenIddict client middleware to redirect the user agent to the identity provider.
                return Challenge(properties, OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
            }
            */

            // For scenarios where the default challenge handler configured in the ASP.NET Core
            // authentication options shouldn't be used, a specific scheme can be specified here.
            return Challenge(new AuthenticationProperties {
                RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters),
            });
        }

        // Retrieve the profile of the logged in user.
        var user = await _users.GetUserAsync(result.Principal)
                ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applications.FindByClientIdAsync(request.ClientId!)
                       ?? throw new InvalidOperationException(
                              "Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizations.FindAsync(await _users.GetUserIdAsync(user),
                                                       (await _applications.GetIdAsync(application))!,
                                                       OpenIddictConstants.Statuses.Valid,
                                                       OpenIddictConstants.AuthorizationTypes.Permanent,
                                                       request.GetScopes())
                                                  .ToListAsync();

        switch (await _applications.GetConsentTypeAsync(application)) {
            // If the consent is external (e.g when authorizations are granted by a sysadmin),
            // immediately return an error if no authorization can be found in the database.
            case OpenIddictConstants.ConsentTypes.External when authorizations.Count is 0:
                return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = OpenIddictConstants.Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "The logged in user is not allowed to access this client application.",
                    }));

            // If the consent is implicit or if an authorization was found,
            // return an authorization response without displaying the consent form.
            case OpenIddictConstants.ConsentTypes.Implicit:
            case OpenIddictConstants.ConsentTypes.External when authorizations.Count is not 0:
            case OpenIddictConstants.ConsentTypes.Explicit when authorizations.Count is not 0
                                                             && !request.HasPrompt(OpenIddictConstants.Prompts.Consent):
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
                identity.SetScopes(request.GetScopes());
                identity.SetResources(await _scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());

                // Automatically create a permanent authorization to avoid requiring explicit consent
                // for future authorization or token requests containing the same scopes.
                var authorization = authorizations.LastOrDefault();
                authorization ??= await _authorizations.CreateAsync(identity, await _users.GetUserIdAsync(user),
                    (await _applications.GetIdAsync(application))!, OpenIddictConstants.AuthorizationTypes.Permanent,
                    identity.GetScopes());

                identity.SetAuthorizationId(await _authorizations.GetIdAsync(authorization));
                identity.SetDestinations(GetDestinations);

                return SignIn(new(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            // At this point, no authorization was found in the database and an error must be returned
            // if the client application specified prompt=none in the authorization request.
            case OpenIddictConstants.ConsentTypes.Explicit when request.HasPrompt(OpenIddictConstants.Prompts.None):
            case OpenIddictConstants.ConsentTypes.Systematic when request.HasPrompt(OpenIddictConstants.Prompts.None):
                return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error]
                            = OpenIddictConstants.Errors.ConsentRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                            = "Interactive user consent is required.",
                    }));

            // In every other case, render the consent form.
            default:

                var response = new AuthorizeResponse {
                    ApplicationName = await _applications.GetLocalizedDisplayNameAsync(application),
                    Scopes = [],
                };

                var scopes = _scopes.FindByNamesAsync(request.GetScopes());
                await foreach (var scope in scopes) {
                    response.Scopes.Add(new() {
                        Name        = await _scopes.GetNameAsync(scope),
                        DisplayName = await _scopes.GetLocalizedDisplayNameAsync(scope),
                        Description = await _scopes.GetLocalizedDescriptionAsync(scope),
                    });
                }

                return Ok(response);
        }
    }

    [Authorize]
    [HttpPost(nameof(Authorize))]
    public async Task<IActionResult> Accept() {
        var request = HttpContext.GetOpenIddictServerRequest()
                   ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Retrieve the profile of the logged in user.
        var user = await _users.GetUserAsync(User)
                ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        // Retrieve the application details from the database.
        var application = await _applications.FindByClientIdAsync(request.ClientId!)
                       ?? throw new InvalidOperationException(
                              "Details concerning the calling client application cannot be found.");

        // Retrieve the permanent authorizations associated with the user and the calling client application.
        var authorizations = await _authorizations.FindAsync(await _users.GetUserIdAsync(user),
                                                       (await _applications.GetIdAsync(application))!,
                                                       OpenIddictConstants.Statuses.Valid,
                                                       OpenIddictConstants.AuthorizationTypes.Permanent,
                                                       request.GetScopes())
                                                  .ToListAsync();

        // Note: the same check is already made in the other action but is repeated
        // here to ensure a malicious user can't abuse this POST-only endpoint and
        // force it to return a valid response without the external authorization.
        if (authorizations.Count is 0
         && await _applications.HasConsentTypeAsync(application, OpenIddictConstants.ConsentTypes.External)) {
            return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new(new Dictionary<string, string?> {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.ConsentRequired,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]
                        = "The logged in user is not allowed to access this client application.",
                }));
        }

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
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await _scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());

        // Automatically create a permanent authorization to avoid requiring explicit consent
        // for future authorization or token requests containing the same scopes.
        var authorization = authorizations.LastOrDefault();
        authorization ??= await _authorizations.CreateAsync(identity, await _users.GetUserIdAsync(user),
            (await _applications.GetIdAsync(application))!, OpenIddictConstants.AuthorizationTypes.Permanent,
            identity.GetScopes());

        identity.SetAuthorizationId(await _authorizations.GetIdAsync(authorization));
        identity.SetDestinations(GetDestinations);

        // Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
        return SignIn(new(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
