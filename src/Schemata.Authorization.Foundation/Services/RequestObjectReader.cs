using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Verifies and decodes JWT-secured authorization request objects, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html#section-4">
///         RFC 9101: JWT-Secured Authorization Request (JAR) §4: Request Object Construction and Validation
///     </seealso>
///     .
/// </summary>
public sealed class RequestObjectReader<TApp>(
    IOptions<JwtSecuredAuthorizationRequestsOptions> options,
    IOptions<SchemataAuthorizationOptions> server,
    IOptions<SchemataSecurityOptions>      security,
    IHttpClientFactory                     http,
    ICacheProvider                         cache,
    ISecurityStore<SchemataSecurity>       securities,
    ClientAssertionChannel                 channel
) where TApp : SchemataApplication
{

    /// <summary>
    ///     Verifies the signed request JWT and folds its claims into <paramref name="target" />,
    ///     replacing any query parameters that the object overrides. JWE (encrypted) request
    ///     objects are rejected with <c>invalid_request_object</c>.
    /// </summary>
    /// <exception cref="OAuthException">
    ///     <c>invalid_request_object</c> on signature, claim, or JWE failures; <c>invalid_request</c>
    ///     when the query <c>client_id</c> does not match the JWT <c>client_id</c>.
    /// </exception>
    public async Task ReadAsync(
        string            assertion,
        TApp              application,
        AuthorizeRequest  target,
        bool              enforceOidcOuterParameters,
        CancellationToken ct
    ) {
        if (assertion.Count(character => character == '.') != 2) {
            throw InvalidRequestObject();
        }
        JsonWebToken jwt;
        try {
            jwt = new JsonWebToken(assertion);
        } catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException) {
            throw new OAuthException(
                OAuthErrors.InvalidRequestObject,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
        }

        if (jwt.EncodedSignature.Length == 0
            && !string.Equals(jwt.Alg, "none", StringComparison.OrdinalIgnoreCase)) {
            throw InvalidRequestObject();
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.Request, out _)
            || jwt.TryGetPayloadValue<string>(Parameters.RequestUri, out _)) {
            // RFC 9101 §4.1: a request object must not itself carry request / request_uri.
            throw new OAuthException(
                OAuthErrors.InvalidRequestObject,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
        }

        var alg = jwt.Alg;
        if (string.Equals(alg, "none", StringComparison.OrdinalIgnoreCase)) {
            if (options.Value.RequireForAllClients || application.RequireSignedRequestObject == true) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequestObject,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
            }
        } else {
            var allowed = ResolveAllowedAlgorithms(application);
            if (allowed.Count == 0 || !allowed.Contains(alg)) {
                throw InvalidRequestObject();
            }

            try {
                var keys = await ClientKeyResolver<TApp>.ResolveAsync(
                               application, assertion, securities, http, cache, security, ct);
                await channel.VerifySignatureAsync(
                    assertion,
                    new() {
                        IssuerSigningKeys = keys,
                        ValidateIssuer    = false,
                        ValidateAudience  = false,
                        ValidateLifetime  = false,
                    });
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            } catch (Exception) {
                throw InvalidRequestObject();
            }
        }

        var clientId = jwt.TryGetPayloadValue<string>(Parameters.ClientId, out var cid) ? cid : null;
        if (string.IsNullOrWhiteSpace(clientId)) {
            throw InvalidRequestObject();
        }

        if (string.IsNullOrWhiteSpace(target.ClientId)
            || !string.Equals(target.ClientId, clientId, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS));
        }

        if (jwt.TryGetPayloadValue<string>(Claims.Issuer, out var issuer)
            && !string.Equals(issuer, clientId, StringComparison.Ordinal)) {
            throw InvalidRequestObject();
        }

        var audiences = jwt.Audiences;
        if (audiences.Any()
            && (string.IsNullOrWhiteSpace(server.Value.Issuer)
                || !audiences.Contains(server.Value.Issuer, StringComparer.Ordinal))) {
            throw InvalidRequestObject();
        }

        var objectScope = jwt.TryGetPayloadValue<string>(Parameters.Scope, out var jarScope) ? jarScope : null;
        if (enforceOidcOuterParameters
            && objectScope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .Contains(Scopes.OpenId, StringComparer.Ordinal) == true) {
            var queryHasOpenId = target.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                              .Contains(Scopes.OpenId, StringComparer.Ordinal) == true;
            var objectResponseType = jwt.TryGetPayloadValue<string>(Parameters.ResponseType, out var jarResponseType)
                ? jarResponseType
                : null;
            if (!queryHasOpenId
                || string.IsNullOrWhiteSpace(target.ResponseType)
                || !string.Equals(target.ResponseType, objectResponseType, StringComparison.Ordinal)) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
            }
        }

        var merged = new AuthorizeRequest {
            ClientId = clientId,
        };

        if (jwt.TryGetPayloadValue<string>(Parameters.RedirectUri, out var redirectUri)) {
            merged.RedirectUri = redirectUri;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.ResponseType, out var responseType)) {
            merged.ResponseType = responseType;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.Scope, out var scope)) {
            merged.Scope = scope;
        }

        if (jwt.TryGetPayloadValue<List<string>>(Parameters.Resource, out var resource)) {
            merged.Resource = resource;
        }

        if (jwt.TryGetPayloadValue<JsonElement>(Parameters.AuthorizationDetails, out var authorizationDetails)) {
            merged.AuthorizationDetails = authorizationDetails.GetRawText();
        }

        if (jwt.TryGetPayloadValue<JsonElement>(Parameters.Claims, out var claims)) {
            merged.Claims = claims.GetRawText();
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.State, out var state)) {
            merged.State = state;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.Nonce, out var nonce)) {
            merged.Nonce = nonce;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.CodeChallenge, out var cc)) {
            merged.CodeChallenge = cc;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.CodeChallengeMethod, out var ccm)) {
            merged.CodeChallengeMethod = ccm;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.DpopJkt, out var dpopJkt)) {
            merged.DpopJkt = dpopJkt;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.ResponseMode, out var rm)) {
            merged.ResponseMode = rm;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.Prompt, out var prompt)) {
            merged.Prompt = prompt;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.Display, out var display)) {
            merged.Display = display;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.LoginHint, out var lh)) {
            merged.LoginHint = lh;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.MaxAge, out var maxAge)) {
            merged.MaxAge = maxAge;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.IdTokenHint, out var ith)) {
            merged.IdTokenHint = ith;
        }

        if (jwt.TryGetPayloadValue<string>(Parameters.AcrValues, out var acr)) {
            merged.AcrValues = acr;
        }


        merged.Request = target.Request;
        merged.RequestUri = target.RequestUri;
        merged.ClientId = clientId;

        foreach (var prop in typeof(AuthorizeRequest).GetProperties()) {
            prop.SetValue(target, prop.GetValue(merged));
        }
    }

    private HashSet<string> ResolveAllowedAlgorithms(TApp application) {
        var server = options.Value.SigningAlgorithms;
        if (string.IsNullOrWhiteSpace(application.RequestObjectSigningAlg)) {
            return new HashSet<string>(server, StringComparer.Ordinal);
        }

        return server.Contains(application.RequestObjectSigningAlg)
            ? new HashSet<string>([application.RequestObjectSigningAlg], StringComparer.Ordinal)
            : [];
    }

    private static OAuthException InvalidRequestObject() {
        return new(
            OAuthErrors.InvalidRequestObject,
            SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
    }
}