using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Pushed Authorization Request endpoint handler, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-3.1">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §3.1: Client Construction
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-3.2">
///         §3.2: Server Processing
///     </seealso>
///     .
/// </summary>
public sealed class ParHandler<TApp>(
    IClientAuthenticationService<TApp>     client,
    ITokenStore<SchemataToken>             tokens,
    IOptions<PushedAuthorizationRequestsOptions> options,
    IOptions<JsonSerializerOptions>        json,
    TimeProvider?                          time = null
) : ParEndpoint
    where TApp : SchemataApplication
{
    private static readonly HashSet<string> JarFormParameters = new(StringComparer.Ordinal) {
        Parameters.Request,
        Parameters.ClientId,
        Parameters.ClientSecret,
        Parameters.ClientAssertionType,
        Parameters.ClientAssertion,
    };

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public override async Task<AuthorizationResult> ParAsync(
        AuthorizeRequest                  request,
        Dictionary<string, List<string?>> form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (!string.IsNullOrWhiteSpace(request.RequestUri)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.RequestUri));
        }

        if (!string.IsNullOrWhiteSpace(request.Request) && form.Keys.Any(key => !JarFormParameters.Contains(key))) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
        }

        var application = await client.AuthenticateAsync(null, form, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS));
        }

        if (!string.IsNullOrWhiteSpace(request.ClientId)
            && !string.Equals(request.ClientId, application.ClientId, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS));
        }

        request.ClientId = application.ClientId;

        var authz = new AuthorizeContext<TApp> {
            Request      = request,
            Application  = application,
            ResponseMode = ResponseModeService.ResolveMode(request.ResponseMode, request.ResponseType),
        };

        var ctx = AdviceContext.Require();
        using var validation = ctx.Use(new ParEndpointValidation());
        switch (await Advisor.For<IAuthorizeRequestAdvisor<TApp>>().RunAsync(ctx, authz, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var normalized):
                return normalized!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST));
        }

        switch (await Advisor.For<IAuthorizeAdvisor<TApp>>().RunAsync(ctx, authz, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var handled):
                return handled!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.ACCESS_DENIED)) {
                    RedirectUri  = authz.Request.RedirectUri,
                    State        = authz.Request.State,
                    ResponseMode = authz.ResponseMode,
                };
        }

        var now        = _time.GetUtcNow().UtcDateTime;
        var expiry     = now + options.Value.Lifetime;
        var random     = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var requestUri = RequestUriPrefixes.Par + random;

        var token = new SchemataToken {
            Name           = Guid.NewGuid().ToString("n"),
            Application    = SecurityParents.Application(application),
            Provider       = TokenTypes.ParRequest,
            Type           = TokenTypes.ParRequest,
            Status         = TokenStatuses.Valid,
            Format         = TokenFormats.Reference,
            ReferenceId    = random,
            Payload        = JsonSerializer.Serialize(request, json.Value),
            ExpireTime     = expiry,
        };
        await tokens.CreateAsync(token, ct);
        return AuthorizationResult.Content(new PushedAuthorizationResponse {
            RequestUri = requestUri,
            ExpiresIn  = (int)options.Value.Lifetime.TotalSeconds,
        });
    }
}

