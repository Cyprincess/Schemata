using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Advisors;
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
                SchemataResources.REGISTRATION_INITIAL_ACCESS_TOKEN_INVALID,
                code: (int)System.Net.HttpStatusCode.Unauthorized
            ) { Headers = new Dictionary<string, string> {
                    ["WWW-Authenticate"] = "Bearer",
                },
            };
            throw unauthorized;
        }

        request = await PrepareRequestAsync(request, ct);

        var application = await RegistrationMetadataMapper.ToApplicationAsync<TApp>(
            request, options, http, ct);
        application.CreateTime = _time.GetUtcNow().UtcDateTime;

        var ctx = AdviceContext.Require();
        await RunRequestAdvisorsAsync(ctx, request, application, ct);

        await ValidateResponseAlgorithmsAsync(application, ct);

        var created = await apps.CreateAsync(application, ct);
        var clientId = created?.ClientId;
        if (created is null || string.IsNullOrWhiteSpace(clientId)) {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata,
                SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }
        if (RequiresClientSecret(created)) {
            var symmetric = created.TokenEndpointAuthMethod == ClientAuthMethods.ClientSecretJwt;
            var secret = RegistrationMetadataMapper.Base64UrlEncode(RandomNumberGenerator.GetBytes(symmetric ? 64 : 32));
            var material = symmetric ? secret : await verifier.HashAsync(secret, ct: ct);
            await securities.CreateAsync(new() {
                Parent    = SecurityParents.Application(created),
                Key       = clientId,
                Kind      = symmetric ? SecurityConstants.Kinds.Secret : SecurityConstants.Kinds.Password,
                Usage     = SecurityConstants.Usages.Authentication,
                Algorithm = symmetric ? created.TokenEndpointAuthSigningAlg ?? SigningAlgorithms.HmacSha256 : SecurityConstants.Algorithms.Pbkdf2,
                Value     = material,
                Status    = SecurityConstants.Statuses.Valid,
            }, ct, (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, created.CanonicalName!, cancellation));
            _plainClientSecret = secret;
        }

        if (request.Jwks is { ValueKind: JsonValueKind.Object } jwks) {
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(created),
                Key    = clientId,
                Kind   = SecurityConstants.Kinds.Jwks,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = jwks.GetRawText(),
                Status = SecurityConstants.Statuses.Valid,
            }, ct, (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, created.CanonicalName!, cancellation));
        }

        if (!string.IsNullOrWhiteSpace(request.JwksUri)) {
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(created),
                Key    = clientId,
                Kind   = SecurityConstants.Kinds.JwksUri,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = request.JwksUri,
                Status = SecurityConstants.Statuses.Valid,
            }, ct, (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, created.CanonicalName!, cancellation));
        }

        var reference = issuer.CreateReference();
        await tokens.CreateAsync(CreateRegistrationToken(created, reference), ct,
            (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, created.CanonicalName!, cancellation));

        var response = await RegistrationMetadataMapper.ToResponse(created, securities, ct);
        response.ClientId                 = created.ClientId;
        response.ClientSecret             = _plainClientSecret;
        response.ClientSecretExpiresAt    = _plainClientSecret is null ? null : 0; // 0 = does not expire
        response.RegistrationAccessToken  = reference;
        response.RegistrationClientUri    = BuildRegistrationClientUri(options.Value, clientId);

        if (await Advisor.For<IRegistrationResponseAdvisor<TApp>>()
                         .RunAsync(ctx, created, response, ct) != AdviseResult.Continue) {
            throw new OAuthException(
                OAuthErrors.InvalidClientMetadata,
                SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        return response;
    }

    public override async Task<RegistrationResponse?> ReadAsync(string? clientId, string? bearerToken, CancellationToken ct) {
        var authorized = await AuthorizeManagementAsync(clientId, bearerToken, ct);
        if (authorized is null) {
            return null;
        }

        var (application, token) = authorized.Value;

        // RFC 7592 §2.1: the read response is the complete current registration - metadata plus
        // the management URI and a usable registration access token.
        var response = await RegistrationMetadataMapper.ToResponse(application, securities, ct);
        var ctx      = AdviceContext.Require();
        if (await Advisor.For<IRegistrationResponseAdvisor<TApp>>()
                          .RunAsync(ctx, application, response, ct) != AdviseResult.Continue) {
            return null;
        }

        response.RegistrationClientUri = BuildRegistrationClientUri(options.Value, application.ClientId!);
        var reference = await CurrentOrRotatedRegistrationTokenAsync(application, token, ct);
        if (reference is null) {
            return null;
        }

        response.RegistrationAccessToken = reference;

        return response;
    }

    public override async Task<RegistrationResponse?> ReplaceAsync(string? clientId, RegisterRequest request, string? bearerToken, CancellationToken ct) {
        var authorized = await AuthorizeManagementAsync(clientId, bearerToken, ct);
        if (authorized is null) {
            return null;
        }

        var (application, token) = authorized.Value;

        // RFC 7592 §3: the four server-managed fields must never appear in an update request.
        if (request.RegistrationAccessToken is not null
         || request.RegistrationClientUri is not null
         || request.ClientIdIssuedAt is not null
         || request.ClientSecretExpiresAt is not null) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.REGISTRATION_FIELDS_SERVER_MANAGED);
        }

        // client_id is required on PUT and must name the client being replaced.
        if (string.IsNullOrWhiteSpace(request.ClientId)
         || !string.Equals(request.ClientId, clientId, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.REGISTRATION_CLIENT_ID_MISMATCH);
        }

        // A submitted client_secret must equal the stored one; clients cannot rotate it here.
        if (!string.IsNullOrWhiteSpace(request.ClientSecret)
         && !await VerifyCurrentClientSecretAsync(application, request.ClientSecret, ct)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.REGISTRATION_CLIENT_SECRET_MISMATCH);
        }

        request = await PrepareRequestAsync(request, ct);
        var nextMethod = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
            ? ClientAuthMethods.ClientSecretBasic : request.TokenEndpointAuthMethod;
        if (nextMethod == ClientAuthMethods.ClientSecretJwt && application.TokenEndpointAuthMethod != nextMethod
         || application.TokenEndpointAuthMethod == ClientAuthMethods.ClientSecretJwt
            && nextMethod is ClientAuthMethods.ClientSecretBasic or ClientAuthMethods.ClientSecretPost) {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }

        // Replace semantics: every writable field is overwritten; omitted fields fall back to
        // their profile default, never to the previously stored value.
        await RegistrationMetadataMapper.ApplyAsync(application, request, options, http, ct);

        var ctx = AdviceContext.Require();
        await RunRequestAdvisorsAsync(ctx, request, application, ct);

        await ValidateResponseAlgorithmsAsync(application, ct);

        await apps.UpdateAsync(application, ct);
        await ReplaceClientKeyMaterialAsync(application, request, ct);

        var response = await RegistrationMetadataMapper.ToResponse(application, securities, ct);
        if (await Advisor.For<IRegistrationResponseAdvisor<TApp>>()
                          .RunAsync(ctx, application, response, ct) != AdviseResult.Continue) {
            return null;
        }

        response.ClientId              = application.ClientId;
        response.RegistrationClientUri = BuildRegistrationClientUri(options.Value, application.ClientId!);
        var reference = await CurrentOrRotatedRegistrationTokenAsync(application, token, ct);
        if (reference is null) {
            return null;
        }

        response.RegistrationAccessToken = reference;

        return response;
    }

    public override async Task<bool> DeleteAsync(string? clientId, string? bearerToken, CancellationToken ct) {
        var authorized = await AuthorizeManagementAsync(clientId, bearerToken, ct);
        if (authorized is null) {
            return false;
        }

        var (application, _) = authorized.Value;

        try {
            await apps.DeleteAsync(application, ct);
        } catch (AbortedException) {
            return false;
        }

        return true;
    }

    #endregion

    /// <summary>
    ///     Prepares a registration access token row without persisting it: the initial POST
    ///     persists through <see cref="ITokenStore{TToken}.CreateAsync" />, while a rotation
    ///     publishes through <see cref="ITokenStore{TToken}.TryRotateAsync" /> so the successor is
    ///     never observable without the predecessor's redemption.
    /// </summary>
    private SchemataToken CreateRegistrationToken(TApp application, string reference) {
        var payload = JsonSerializer.Serialize(new RegistrationTokenPayload {
            ClientId  = application.ClientId,
            IssuedAt  = _time.GetUtcNow().ToUnixTimeSeconds(),
        });

        return new() {
            // Non-user artifact: Parent stays null so logout fan-outs keyed by subject never see it (spec §3.3).
            Parent       = null,
            Application  = application.CanonicalName,
            Type         = TokenTypes.Registration,
            Status       = TokenStatuses.Valid,
            Format       = TokenFormats.Reference,
            ReferenceId  = reference,
            Payload      = payload,
            // RFC 7592 §5: the token stays valid for as long as the client remains registered
            // unless the host configured a finite lifetime; a finite expiry is always enforced
            // and applies unchanged to every rotated successor.
            ExpireTime   = options.Value.RegistrationTokenLifetime is { } lifetime
                ? _time.GetUtcNow().UtcDateTime.Add(lifetime)
                : null,
        };
    }

    private static string BuildRegistrationClientUri(SchemataAuthorizationOptions options, string clientId) {
        return $"{CanonicalIssuer.Combine(options.Issuer, Endpoints.Register)}/{clientId}";
    }

    /// <summary>
    ///     Authenticates a management request: the bearer must be a valid, unexpired registration
    ///     token bound to exactly the named client, and that client must still exist.
    /// </summary>
    private async Task<(TApp Application, SchemataToken Token)?> AuthorizeManagementAsync(string? clientId, string? bearerToken, CancellationToken ct) {
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
        if (application is null) {
            return null;
        }

        return (application, token);
    }

    /// <summary>
    ///     Returns the registration token the management response carries: by default the current
    ///     reference (reference tokens are stored verbatim, so it stays usable), or a rotated one
    ///     when the host enabled rotation. The token store redeems the predecessor and publishes
    ///     the successor in one commit; a lost rotation returns <see langword="null" /> so the
    ///     loser never receives the now-redeemed predecessor, which the controller maps to 401.
    /// </summary>
    private async Task<string?> CurrentOrRotatedRegistrationTokenAsync(TApp application, SchemataToken current, CancellationToken ct) {
        if (!options.Value.RotateRegistrationTokens) {
            return current.ReferenceId;
        }

        var successor = CreateRegistrationToken(application, issuer.CreateReference());
        return await tokens.TryRotateAsync(current, [successor], ct,
            (transaction, cancellation) => apps.EnlistTokenPublicationAsync(transaction, [successor], cancellation))
            ? successor.ReferenceId
            : null;
    }

    private async Task<bool> VerifyCurrentClientSecretAsync(TApp application, string secret, CancellationToken ct) {
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Application(application),
                           application.TokenEndpointAuthMethod == ClientAuthMethods.ClientSecretJwt
                               ? SecurityConstants.Kinds.Secret : SecurityConstants.Kinds.Password,
                           SecurityConstants.Usages.Authentication, SecurityConstants.Statuses.Valid, ct)) {
            return await verifier.VerifyAsync(row, secret, ct);
        }

        return false;
    }

    /// <summary>Replaces the client's jwks / jwks_uri rows so they match the replaced metadata.</summary>
    private async Task ReplaceClientKeyMaterialAsync(TApp application, RegisterRequest request, CancellationToken ct) {
        var parent = SecurityParents.Application(application);

        // The listing streams and updates share the store's repository context, so both kind
        // listings are drained before any update.
        var stale = new List<SchemataSecurity>();
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, null, ct)) {
            stale.Add(row);
        }

        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, null, ct)) {
            stale.Add(row);
        }

        foreach (var row in stale) {
            row.Status = SecurityConstants.Statuses.Revoked;
            await securities.UpdateAsync(row, ct);
        }

        if (request.Jwks is { ValueKind: JsonValueKind.Object } jwks) {
            await securities.CreateAsync(new() {
                Parent = parent,
                Key    = application.ClientId,
                Kind   = SecurityConstants.Kinds.Jwks,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = jwks.GetRawText(),
                Status = SecurityConstants.Statuses.Valid,
            }, ct, (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, application.CanonicalName!, cancellation));
        }

        if (!string.IsNullOrWhiteSpace(request.JwksUri)) {
            await securities.CreateAsync(new() {
                Parent = parent,
                Key    = application.ClientId,
                Kind   = SecurityConstants.Kinds.JwksUri,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = request.JwksUri,
                Status = SecurityConstants.Statuses.Valid,
            }, ct, (transaction, cancellation) => apps.EnlistPublicationAsync(transaction, application.CanonicalName!, cancellation));
        }
    }

    /// <summary>
    ///     Full software-statement validation and claim overlay shared by create and replace. A
    ///     statement that is not a well-formed JWT is malformed (<c>invalid_software_statement</c>);
    ///     a well-formed one is rejected unless the host trusts its issuer
    ///     (<c>unapproved_software_statement</c>). Statement claims whose shapes do not match the
    ///     metadata fields surface as <c>invalid_client_metadata</c>, never as uncaught
    ///     deserialization failures crossing the endpoint.
    /// </summary>
    private async Task<RegisterRequest> PrepareRequestAsync(RegisterRequest request, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(request.SoftwareStatement)) {
            return request;
        }

        try {
            _ = new JsonWebToken(request.SoftwareStatement);
        } catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException) {
            throw new OAuthException(OAuthErrors.InvalidSoftwareStatement,
                SchemataResources.SOFTWARE_STATEMENT_NOT_WELL_FORMED);
        }

        var validation = softwareStatements is null
            ? SoftwareStatementValidationResult.Unapproved
            : await softwareStatements.ValidateAndExtractAsync(request.SoftwareStatement, ct);
        if (!validation.IsValid) {
            throw new OAuthException(OAuthErrors.InvalidSoftwareStatement,
                SchemataResources.SOFTWARE_STATEMENT_NOT_WELL_FORMED);
        }
        if (validation.Claims is not { } claims) {
            throw new OAuthException(OAuthErrors.UnapprovedSoftwareStatement,
                SchemataResources.SOFTWARE_STATEMENT_ISSUER_NOT_APPROVED);
        }

        // RFC 7591 §2.3: metadata asserted by a validated statement takes precedence over the
        // same-named plain request values; untrusted body values never override it.
        try {
            return ApplySoftwareStatement(request, claims);
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NotSupportedException) {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }
    }

    /// <summary>
    ///     Runs the registration request advisors once; a non-Continue verdict rejects the
    ///     registration with <c>invalid_client_metadata</c> on both the create and replace paths.
    /// </summary>
    private static async Task RunRequestAdvisorsAsync(
        AdviceContext   ctx,
        RegisterRequest request,
        TApp            application,
        CancellationToken ct
    ) {
        if (await Advisor.For<IRegistrationRequestAdvisor<TApp>>()
                         .RunAsync(ctx, request, application, ct) != AdviseResult.Continue) {
            throw new OAuthException(
                OAuthErrors.InvalidClientMetadata,
                SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }
    }

    private static RegisterRequest ApplySoftwareStatement(
        RegisterRequest                                            request,
        IDictionary<string, JsonElement> claims
    ) {
        var serverManaged = new HashSet<string>(StringComparer.Ordinal) {
            "client_id", "client_secret", "client_secret_expires_at", "client_id_issued_at",
            "registration_access_token", "registration_client_uri", "software_statement",
        };

        using var merged = JsonSerializer.SerializeToDocument(request, MetadataJson);
        var root = merged.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        foreach (var (key, value) in claims) {
            if (!serverManaged.Contains(key)) {
                root[key] = JsonSerializer.Serialize(value, MetadataJson);
            }
        }

        var json = "{" + string.Join(",", root.Select(kv => JsonSerializer.Serialize(kv.Key, MetadataJson) + ":" + kv.Value)) + "}";
        return JsonSerializer.Deserialize<RegisterRequest>(json, MetadataJson)!;
    }

    private static readonly JsonSerializerOptions MetadataJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static bool RequiresClientSecret(SchemataApplication application) {
        return application.TokenEndpointAuthMethod == ClientAuthMethods.ClientSecretJwt
            || application.TokenEndpointAuthMethod is ClientAuthMethods.ClientSecretBasic or ClientAuthMethods.ClientSecretPost
                && application.IsConfidential;
    }

    /// <summary>Checks current signing capability for the client's protected responses.</summary>
    private async Task ValidateResponseAlgorithmsAsync(TApp application, CancellationToken ct) {
        var idAlgorithm = SchemataApplicationMetadata.HasScope(application, Scopes.OpenId)
            ? application.IdTokenSignedResponseAlg ?? SigningAlgorithms.RsaSha256 : null;
        var userInfoAlgorithm = application.UserinfoSignedResponseAlg;
        var needsId = !string.IsNullOrWhiteSpace(idAlgorithm);
        var needsUserInfo = !string.IsNullOrWhiteSpace(userInfoAlgorithm);
        if (!needsId && !needsUserInfo) return;
        if (idAlgorithm == "none" || userInfoAlgorithm == "none") {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(options.Value.Issuer!), null, SecurityConstants.Usages.Signing,
                           SecurityConstants.Statuses.Valid, ct)) {
            var algorithm = SecurityKeyAdapter.ToSigningAlgorithm(row.Algorithm);
            if (algorithm == idAlgorithm) needsId = false;
            if (algorithm == userInfoAlgorithm) needsUserInfo = false;
        }
        if (needsId || needsUserInfo) {
            throw new OAuthException(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }
    }
}
