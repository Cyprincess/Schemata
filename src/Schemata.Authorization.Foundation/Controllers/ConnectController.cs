// Some code is borrowed from OpenIddict
// https://github.com/openiddict/openiddict-core/blob/b32eb8c0a29c9e1cccb15e5d2ac2f6d4f8b7243b/sandbox/OpenIddict.Sandbox.AspNetCore.Server/Controllers/AuthorizationController.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Controllers;

[ApiController]
[Route("~/[controller]")]
public partial class ConnectController : ControllerBase
{
    private readonly IOpenIddictApplicationManager     _applications;
    private readonly IOpenIddictAuthorizationManager   _authorizations;
    private readonly IOpenIddictScopeManager           _scopes;
    private readonly SignInManager<SchemataUser>       _sign;
    private readonly SchemataUserManager<SchemataUser> _users;

    public ConnectController(
        IOpenIddictApplicationManager     applications,
        IOpenIddictAuthorizationManager   authorizations,
        IOpenIddictScopeManager           scopes,
        SignInManager<SchemataUser>       sign,
        SchemataUserManager<SchemataUser> users) {
        _applications   = applications;
        _authorizations = authorizations;
        _scopes         = scopes;
        _sign           = sign;
        _users          = users;
    }

    private IEnumerable<string> GetDestinations(Claim claim) {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

        switch (claim.Type) {
            case OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.PreferredUsername:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Permissions.Scopes.Profile) == true) {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.Nickname:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Permissions.Scopes.Profile) == true) {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.Email:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Permissions.Scopes.Email) == true) {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.PhoneNumber:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Permissions.Scopes.Phone) == true) {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            case OpenIddictConstants.Claims.Role:
                yield return OpenIddictConstants.Destinations.AccessToken;

                if (claim.Subject?.HasScope(OpenIddictConstants.Permissions.Scopes.Roles) == true) {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield break;
        }
    }
}
