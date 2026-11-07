using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>Handles the RFC 6749 §4.3 resource owner password credentials grant.</summary>
public sealed class ResourceOwnerPasswordHandler<TApp>(
    IClientAuthenticationService<TApp> client,
    IServiceProvider services
) : IGrantHandler
    where TApp : SchemataApplication
{
    /// <inheritdoc />
    public string GrantType => GrantTypes.Password;

    /// <inheritdoc />
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest request,
        Dictionary<string, List<string?>>? headers,
        string? proof,
        AdviceContext context,
        CancellationToken ct
    ) {
        var username = ValidateCredentialGrammar(request.Username, "username");
        var password = ValidateCredentialGrammar(request.Password, "password");

        var authentication = await client.AuthenticateAsync(null, ClientAuthenticationForm.Build(
            request.ClientId, request.ClientSecret, request.ClientAssertion, request.ClientAssertionType), headers, ct);
        var application = authentication?.Application;
        if (string.IsNullOrWhiteSpace(application?.ClientId)
         || application.IsConfidential && authentication?.Authenticated != true) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var tokenContext = new TokenRequestContext<TApp> {
            Request = request,
            Application = application,
            Proof = proof,
        };

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>().RunAsync(context, tokenContext, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when tokenContext.Result is { } granted:
                GrantCredentials.Attach(granted, tokenContext.Binding, null);
                return granted;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var validator = services.GetService<IResourceOwnerPasswordValidator>();
        if (validator is null) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var principal = await validator.ValidateAsync(application, username, password, ct);
        if (principal?.Identity is not ClaimsIdentity source
         || string.IsNullOrWhiteSpace(principal.FindFirst(IdentityClaims.Subject)?.Value)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var claims = principal.Claims.ToList();
        claims.Add(new(Claims.ClientId, application.ClientId));
        var identity = new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer,
            source.NameClaimType, source.RoleClaimType);
        var result = AuthorizationResult.SignIn(new ClaimsPrincipal(identity), new() {
            [Properties.GrantType] = GrantTypes.Password,
            [Properties.Scope] = request.Scope,
            [Properties.Resources] = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
        });
        GrantCredentials.Attach(result, tokenContext.Binding, null);
        return result;
    }

    private static string ValidateCredentialGrammar(string? value, string parameter) {
        if (value is null || value.Contains('\r') || value.Contains('\n')) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST,
                new Dictionary<string, string?> { ["value"] = parameter });
        }

        return value;
    }
}
