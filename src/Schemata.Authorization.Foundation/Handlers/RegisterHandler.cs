using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Dynamic client registration endpoint implementation, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///         OpenID Connect Dynamic Client Registration 1.0 §3: Client Registration Endpoint
///     </seealso>
///     .
/// </summary>
public sealed class RegisterHandler<TApp>(
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>                    tokens,
    TokenService                           issuer,
    IOptions<SchemataAuthorizationOptions> options,
    IHttpClientFactory                     http,
    ISecurityStore<SchemataSecurity>       securities,
    ISecretVerifier                        verifier,
    ISoftwareStatementValidator?           softwareStatements = null,
    IInitialAccessTokenValidator?          initialAccess      = null,
    TimeProvider?                          time               = null
) : RegisterEndpoint
    where TApp : SchemataApplication, new()
{
    /// <summary>Default registration access token lifetime: 31 days (RFC 7592 §2.3.1 practice).</summary>
    public static readonly TimeSpan RegistrationTokenLifetime = TimeSpan.FromDays(31);

    private string? _plainClientSecret;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region RegisterEndpoint Members

    public override async Task<RegistrationResponse> HandleAsync(RegisterRequest request, string? bearerToken, CancellationToken ct) {
        // Soft deny: no host-supplied validator means no initial access token is trusted, so
        // anonymous and token-bearing registration requests alike are rejected.
        var approved = initialAccess is not null && await initialAccess.ValidateAsync(bearerToken, ct);
        if (!approved) {
            // RFC 7591 §3.2.2 / RFC 6750 §3: unauthorized registration is rejected with 401 + a Bearer challenge.
            var unauthorized = new OAuthException(
                OAuthErrors.InvalidToken,
                "The initial access token is invalid or absent.",
                (int)System.Net.HttpStatusCode.Unauthorized
            );
            unauthorized.Headers = new System.Collections.Generic.Dictionary<string, string> {
                ["WWW-Authenticate"] = "Bearer",
            };
            throw unauthorized;
        }

        if (!string.IsNullOrWhiteSpace(request.SoftwareStatement)) {
            // A statement that is not a well-formed JWT is malformed (invalid_software_statement);
            // a well-formed one is rejected unless the host trusts its issuer (unapproved_software_statement).
            var segments = request.SoftwareStatement.Split('.');
            if (segments.Length is < 3 or > 5) {
                throw new OAuthException(OAuthErrors.InvalidSoftwareStatement,
                    "software_statement is not a well-formed JWS.");
            }

            if (softwareStatements is null
             || !await softwareStatements.ValidateAsync(request.SoftwareStatement, ct)) {
                throw new OAuthException(OAuthErrors.UnapprovedSoftwareStatement,
                    "software_statement issuer is not approved.");
            }
        }

        var application = await RegistrationMetadataMapper.ToApplicationAsync<TApp>(request, options, http, securities, _time, ct);

        await ValidateUserInfoResponseAlgorithmsAsync(application, ct);

        if (RequiresClientSecret(application)) {
            var secret = RegistrationMetadataMapper.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var hash   = await verifier.HashAsync(secret, ct: ct);
            await securities.CreateAsync(new() {
                Parent    = SecurityParents.Application(application),
                Name      = application.ClientId,
                Kind      = SecurityConstants.Kinds.Password,
                Usage     = SecurityConstants.Usages.Authentication,
                Algorithm = SecurityConstants.Algorithms.Pbkdf2,
                Value     = hash,
                Status    = SecurityConstants.Statuses.Valid,
            }, ct);
            _plainClientSecret = secret;
        }

        var created = await apps.CreateAsync(application, ct);
        var clientId = created?.ClientId;
        if (created is null || string.IsNullOrWhiteSpace(clientId)) {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS));
        }

        var now     = _time.GetUtcNow().ToUnixTimeSeconds();
        var reference = issuer.CreateReference();
        await IssueRegistrationTokenAsync(created, reference, ct);

        var response = await RegistrationMetadataMapper.ToResponse(created, securities, ct);
        response.ClientId                 = created.ClientId;
        response.ClientIdIssuedAt         = now;
        response.ClientSecret             = _plainClientSecret;
        response.ClientSecretExpiresAt    = _plainClientSecret is null ? null : 0; // 0 = does not expire
        response.RegistrationAccessToken  = reference;
        response.RegistrationClientUri    = BuildRegistrationClientUri(options.Value, clientId);

        return response;
    }

    public override async Task<RegistrationResponse?> ReadAsync(string? clientId, string? bearerToken, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(clientId)) {
            return null;
        }

        var token = await tokens.FindByReferenceIdAsync(bearerToken, ct);
        if (token?.Type != TokenTypes.Registration
            || token.Status != TokenStatuses.Valid
            || token.ExpireTime is { } expiry && expiry <= _time.GetUtcNow().UtcDateTime) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(token.Payload)) {
            return null;
        }

        string bound;
        try {
            bound = JsonSerializer.Deserialize<RegistrationTokenPayload>(token.Payload)?.ClientId ?? string.Empty;
        } catch (JsonException) {
            return null;
        }

        if (!string.Equals(bound, clientId, StringComparison.Ordinal)) {
            return null;
        }

        var application = await apps.FindByClientIdAsync(clientId, ct);
        return application is null ? null : await RegistrationMetadataMapper.ToResponse(application, securities, ct);
    }

    #endregion

    private async Task IssueRegistrationTokenAsync(TApp application, string reference, CancellationToken ct) {
        var payload = JsonSerializer.Serialize(new RegistrationTokenPayload {
            ClientId  = application.ClientId,
            IssuedAt  = _time.GetUtcNow().ToUnixTimeSeconds(),
        });

        var token = new SchemataToken {
            // Non-user artifact: Parent stays null so logout fan-outs keyed by subject never see it (spec §3.3).
            Parent       = null,
            Application  = application.CanonicalName ?? $"applications/{application.ClientId}",
            Type         = TokenTypes.Registration,
            Status       = TokenStatuses.Valid,
            Format       = TokenFormats.Reference,
            ReferenceId  = reference,
            Payload      = payload,
            ExpireTime   = _time.GetUtcNow().UtcDateTime.Add(RegistrationTokenLifetime),
        };

        await tokens.CreateAsync(token, ct);
    }

    private static string BuildRegistrationClientUri(SchemataAuthorizationOptions options, string clientId) {
        return $"{options.Issuer!.TrimEnd('/')}{Endpoints.Register}/{clientId}";
    }

    private static bool RequiresClientSecret(SchemataApplication application) {
        return application.TokenEndpointAuthMethod is ClientAuthMethods.ClientSecretBasic or ClientAuthMethods.ClientSecretPost
            && application.ClientType == ClientTypes.Confidential;
    }

    /// <summary>
    ///     Cross-checks the UserInfo response protection metadata against the issuer's key
    ///     material: <c>userinfo_signed_response_alg</c> must equal the active signing
    ///     algorithm (the response is signed with the issuer's primary signing row), and
    ///     <c>userinfo_encrypted_response_alg</c> requires registered client key material.
    /// </summary>
    private async Task ValidateUserInfoResponseAlgorithmsAsync(TApp application, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(application.UserinfoSignedResponseAlg)) {
            return;
        }

        SchemataSecurity? primary = null;
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(options.Value.Issuer!), null, SecurityConstants.Usages.Signing, null, ct)) {
            if (row.Status == SecurityConstants.Statuses.Valid) {
                primary = row;
                break;
            }
        }

        var active = primary is null
            ? null
            : SecurityKeyAdapter.ToSigningAlgorithm(primary.Algorithm);
        if (!string.Equals(active, application.UserinfoSignedResponseAlg, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidClientMetadata,
                $"userinfo_signed_response_alg {application.UserinfoSignedResponseAlg} does not match the issuer's active signing algorithm {active ?? "(none)"}.");
        }
    }
}
