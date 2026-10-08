using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>client_credentials</c> grant type.
///     Authenticates the client, runs the <see cref="ITokenRequestAdvisor{TApp}" /> pipeline,
///     and emits a <see cref="AuthorizationResult.SignIn" /> with the client_id claim.
///     No user subject is associated — the client is the resource owner,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.1">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.1
///     </seealso>
///     .
/// </summary>
public sealed class ClientCredentialsHandler<TApp>(IClientAuthenticationService<TApp> client) : IGrantHandler
    where TApp : SchemataApplication
{
    #region IGrantHandler Members

    public string GrantType => GrantTypes.ClientCredentials;

    /// <summary>
    ///     Issues tokens on behalf of a confidential client using client credentials.
    /// </summary>
    /// <param name="request">Token request containing client credentials.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var authentication = await client.AuthenticateAsync(null, ClientAuthenticationForm.Build(
                                                              request.ClientId, request.ClientSecret,
                                                              request.ClientAssertion, request.ClientAssertionType), headers, ct);
        var application = authentication?.Application;
        // RFC 6749 §4.4.2: the client credentials grant requires a confidential client whose
        // credential was actually verified; a public client — identified through none or
        // presenting no secret at all — is never admitted as an authenticated authority.
        if (application is null || !application.IsConfidential || authentication is not { Authenticated: true }) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var ctx = AdviceContext.Require();

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var claims = new List<Claim> {
            new(Claims.ClientId, application.ClientId!),
        };

        // RFC 9068 §5: a JWT access token always carries a sub; for grants without a resource
        // owner - client credentials - it identifies the client itself. The framework's subject
        // space is canonical resource names (the token row's Parent is a resource reference), so
        // the application's canonical name is the client subject, kept distinct from user
        // subjects (users/{x}) by its applications/{x} form.
        if (!string.IsNullOrWhiteSpace(application.CanonicalName)) {
            claims.Add(new(IdentityClaims.Subject, application.CanonicalName));
        }

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = request.Scope,
            [Properties.Resources] = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
        });
    }

    #endregion
}
