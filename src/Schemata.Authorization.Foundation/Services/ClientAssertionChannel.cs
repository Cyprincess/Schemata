using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Shared request handling for the assertion-based client authentication channels
///     (<c>client_secret_jwt</c>, <c>private_key_jwt</c>), per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7521.html#section-4.2">
///         RFC 7521: Assertion Framework for OAuth 2.0 Client Authentication and
///         Authorization Grants §4.2: Using Assertions for Client Authentication
///     </seealso>
///     .
/// </summary>
public sealed class ClientAssertionChannel
{
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>Recognizes a request presenting a jwt-bearer client assertion.</summary>
    public bool Presents(Dictionary<string, List<string?>>? form) {
        if (form is null) {
            return false;
        }

        return form.TryGetValue(Parameters.ClientAssertionType, out var types)
            && types.Count == 1
            && types[0] == ClientAssertionTypes.JwtBearer
            && form.TryGetValue(Parameters.ClientAssertion, out var assertions)
            && assertions.Count == 1
            && !string.IsNullOrWhiteSpace(assertions[0]);
    }

    /// <summary>
    ///     Resolves the client identifier.  RFC 7521 §4.2 makes <c>client_id</c> optional:
    ///     the client is identified by the assertion subject, and a <c>client_id</c> that is
    ///     present must identify the same client — the validator rejects mismatches.
    /// </summary>
    public string ResolveClientId(Dictionary<string, List<string?>>? form, string assertion) {
        if (form is not null && form.TryGetValue(Parameters.ClientId, out var ids)) {
            if (ids.Count != 1) {
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId)
                );
            }

            var id = ids[0];
            if (!string.IsNullOrWhiteSpace(id)) {
                return id;
            }
        }

        var subject = Peek(assertion)?.Subject;
        if (string.IsNullOrWhiteSpace(subject)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ASSERTION_CLIENT_ID_REQUIRED)
            );
        }

        return subject;
    }

    public async Task<TApp> FindApplicationAsync<TApp>(
        IApplicationManager<TApp> apps,
        string                    clientId,
        CancellationToken         ct
    ) where TApp : SchemataApplication {
        var app = await apps.FindByClientIdAsync(clientId, ct);
        if (app is null) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }

        return app;
    }

    /// <summary>
    ///     Audience values identifying this authorization server: the issuer and the token
    ///     endpoint URL, per RFC 7523 §3.
    /// </summary>
    public IReadOnlyList<string> Audiences(SchemataAuthorizationOptions options) {
        var issuer = options.Issuer;
        return string.IsNullOrEmpty(issuer) ? [] : [issuer, issuer + Endpoints.Token];
    }

    /// <summary>
    ///     Verifies the assertion signature.  Every other check (structure, claims, algorithm,
    ///     replay) already ran in the validator, so the parameters disable those validations.
    /// </summary>
    public async Task VerifySignatureAsync(string assertion, TokenValidationParameters parameters) {
        var result = await Handler.ValidateTokenAsync(assertion, parameters);
        if (!result.IsValid) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }
    }

    /// <summary>Parses the assertion without trusting it; returns null when it is not a well-formed JWT.</summary>
    public JsonWebToken? Peek(string assertion) {
        try {
            return new(assertion);
        } catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException) {
            return null;
        }
    }
}
