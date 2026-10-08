using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Maps <see cref="RegisterRequest" /> wire metadata onto <see cref="SchemataApplication" /> and back,
///     enforcing the OIDC Dynamic Client Registration §2 metadata constraints, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///         OpenID Connect Dynamic Client Registration 1.0 §2: Client Metadata
///     </seealso>
///     .
/// </summary>
public static class RegistrationMetadataMapper
{
    /// <summary>
    ///     The grant-type-to-response-type correspondence table from OIDC Dynamic Client
    ///     Registration §2: every <c>response_type</c> requires its backing grant types.
    /// </summary>
    private static readonly Dictionary<string, string[]> ResponseTypeRequirements = new() {
        [ResponseTypes.Code] = [GrantTypes.AuthorizationCode],
    };

    /// <summary>Validates registration metadata and maps it onto a new application with an OAuth client identifier.</summary>
    public static async Task<TApp> ToApplicationAsync<TApp>(
        RegisterRequest                        request,
        IOptions<SchemataAuthorizationOptions> options,
        IHttpClientFactory                     http,
        CancellationToken                      ct   = default
    )
        where TApp : SchemataApplication, new()
    {
        var application = new TApp {
            ClientId = GenerateClientId(),
        };

        await ApplyAsync(application, request, options, http, ct);

        return application;
    }

    /// <summary>
    ///     Validates registration metadata and replaces every writable field of
    ///     <paramref name="application" /> with it: omitted fields become their profile default
    ///     (or null), never a merge with the previous values - RFC 7592 §3 replace semantics. The
    ///     identifier, timestamps, and concurrency token are untouched.
    /// </summary>
    public static async Task ApplyAsync<TApp>(
        TApp                                   application,
        RegisterRequest                        request,
        IOptions<SchemataAuthorizationOptions> options,
        IHttpClientFactory                     http,
        CancellationToken                      ct   = default
    )
        where TApp : SchemataApplication
    {
        var redirectFlow = UsesRedirectFlow(request);

        ValidateApplicationType(request);
        ValidateAuthMethod(request, options);
        ValidateJwksPairing(request);
        ValidateDefaultMaxAge(request);
        if (redirectFlow) {
            // Interactive profiles keep the redirect obligations; a pure OAuth registration
            // (e.g. client_credentials) carries none and never fetches a sector document.
            ValidateRedirectUris(request, options.Value);
            await ValidateSectorIdentifierAsync(request, http, ct);
            ValidateUriHostConsistency(request);
        }
        ValidateLogoutUris(request, options.Value);

        var (grantTypes, responseTypes) = NormalizeGrantAndResponseTypes(request, redirectFlow);

        var applicationType = string.IsNullOrWhiteSpace(request.ApplicationType) ? ApplicationTypes.Web : request.ApplicationType;

        application.ApplicationType            = applicationType;
        application.GrantTypes                 = grantTypes;
        application.ResponseTypes              = responseTypes;
        application.Scope                      = NormalizeScope(request.Scope);
        application.IdTokenSignedResponseAlg   = request.IdTokenSignedResponseAlg ?? SigningAlgorithms.RsaSha256;
        application.RedirectUris               = request.RedirectUris;
        application.PostLogoutRedirectUris     = request.PostLogoutRedirectUris;
        application.ClientName                 = request.ClientName;
        application.Contacts                   = request.Contacts;
        application.ClientUri                  = request.ClientUri;
        application.LogoUri                    = request.LogoUri;
        application.PolicyUri                  = request.PolicyUri;
        application.TosUri                     = request.TosUri;
        application.TokenEndpointAuthMethod    = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
                                                      ? ClientAuthMethods.ClientSecretBasic
                                                      : request.TokenEndpointAuthMethod;
        application.TokenEndpointAuthSigningAlg = request.TokenEndpointAuthSigningAlg;
        application.UserinfoSignedResponseAlg = request.UserinfoSignedResponseAlg;
        application.UserinfoEncryptedResponseAlg = request.UserinfoEncryptedResponseAlg;
        application.UserinfoEncryptedResponseEnc = request.UserinfoEncryptedResponseEnc;
        application.SubjectType = request.SubjectType;
        application.SectorIdentifierUri = request.SectorIdentifierUri;
        application.DefaultMaxAge = request.DefaultMaxAge?.ToString(CultureInfo.InvariantCulture);
        application.RequireAuthTime = request.RequireAuthTime ?? false;
        application.DefaultAcrValues = request.DefaultAcrValues;
        application.InitiateLoginUri = request.InitiateLoginUri;
        application.FrontChannelLogoutUri = request.FrontChannelLogoutUri;
        application.FrontChannelLogoutSessionRequired = request.FrontChannelLogoutSessionRequired ?? false;
        application.BackChannelLogoutUri = request.BackChannelLogoutUri;
        application.BackChannelLogoutSessionRequired = request.BackChannelLogoutSessionRequired ?? false;
        application.SoftwareId = request.SoftwareId;
        application.SoftwareVersion = request.SoftwareVersion;
        application.SoftwareStatement = request.SoftwareStatement;
        application.DisplayNames = null;
        application.LocalizedMetadata = null;
        if (request.LocalizedMetadata is { } localized) {
            foreach (var (name, value) in localized) {
                var separator = name.IndexOf('#');
                if (separator <= 0 || separator == name.Length - 1) continue;
                var field = name[..separator];
                if (field is not ("client_name" or "client_uri" or "logo_uri" or "policy_uri" or "tos_uri")) continue;
                if (value.ValueKind != JsonValueKind.String) {
                    throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
                }
                if (field == "client_name") {
                    (application.DisplayNames ??= new())[name[(separator + 1)..]] = value.GetString();
                } else {
                    (application.LocalizedMetadata ??= new())[name] = value.GetString()!;
                }
            }
        }

        // Protocol metadata lives in its typed fields; Permissions carries only
        // administrator-granted permission entries (e: endpoints).
    }

    /// <summary>Maps a stored application back onto the wire response shape, echoing the
    /// client's newest <c>jwks</c> / <c>jwks_uri</c> security rows.</summary>
    public static async Task<RegistrationResponse> ToResponse(
        SchemataApplication              application,
        ISecurityStore<SchemataSecurity> securities,
        CancellationToken                ct = default
    ) {
        var parent = SecurityParents.Application(application);

        JsonElement? jwks = null;
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, SecurityConstants.Statuses.Valid, ct)) {
            if (string.IsNullOrWhiteSpace(row.Value)) {
                throw new InvalidOperationException("The active registration JWKS row has no key document.");
            }

            using var document = JsonDocument.Parse(row.Value);
            ValidatePublicJwks(document.RootElement);
            jwks = document.RootElement.Clone();
            break;
        }

        string? jwksUri = null;
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, SecurityConstants.Statuses.Valid, ct)) {
            jwksUri = row.Value;
            break;
        }

        Dictionary<string, JsonElement>? localized = null;
        if (application.DisplayNames is { } names) {
            foreach (var (language, value) in names) (localized ??= new())["client_name#" + language] = JsonSerializer.SerializeToElement(value);
        }
        if (application.LocalizedMetadata is { } metadata) {
            foreach (var (name, value) in metadata) {
                var separator = name.IndexOf('#');
                if (separator <= 0 || separator == name.Length - 1
                    || name[..separator] is not ("client_uri" or "logo_uri" or "policy_uri" or "tos_uri")) continue;
                (localized ??= new())[name] = JsonSerializer.SerializeToElement(value);
            }
        }

        return new() {
            ClientId                                = application.ClientId,
            ClientIdIssuedAt                         = application.CreateTime is { } created
                ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)).ToUnixTimeSeconds() : null,
            LocalizedMetadata                       = localized,
            RedirectUris                            = application.RedirectUris?.ToList(),
            PostLogoutRedirectUris                  = application.PostLogoutRedirectUris?.ToList(),
            ClientName                              = application.ClientName,
            Contacts                                = application.Contacts?.ToList(),
            ClientUri                               = application.ClientUri,
            LogoUri                                 = application.LogoUri,
            PolicyUri                               = application.PolicyUri,
            TosUri                                  = application.TosUri,
            Jwks                                    = jwks,
            JwksUri                                 = jwksUri,
            TokenEndpointAuthMethod                 = application.TokenEndpointAuthMethod,
            TokenEndpointAuthSigningAlg             = application.TokenEndpointAuthSigningAlg,
            UserinfoSignedResponseAlg               = application.UserinfoSignedResponseAlg,
            UserinfoEncryptedResponseAlg            = application.UserinfoEncryptedResponseAlg,
            UserinfoEncryptedResponseEnc            = application.UserinfoEncryptedResponseEnc,
            ApplicationType                         = application.ApplicationType,
            SubjectType                             = application.SubjectType,
            SectorIdentifierUri                     = application.SectorIdentifierUri,
            DefaultMaxAge = long.TryParse(application.DefaultMaxAge, NumberStyles.None, CultureInfo.InvariantCulture, out var maxAge) ? maxAge : null,
            RequireAuthTime                         = application.RequireAuthTime,
            DefaultAcrValues                        = application.DefaultAcrValues?.ToList(),
            InitiateLoginUri                        = application.InitiateLoginUri,
            FrontChannelLogoutUri                   = application.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired       = application.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri                    = application.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired        = application.BackChannelLogoutSessionRequired,
            SoftwareId                              = application.SoftwareId,
            SoftwareVersion                         = application.SoftwareVersion,
            SoftwareStatement                       = application.SoftwareStatement,
            IdTokenSignedResponseAlg                = application.IdTokenSignedResponseAlg,
            GrantTypes                              = application.GrantTypes?.ToList(),
            ResponseTypes                           = application.ResponseTypes?.ToList(),
            Scope                                   = application.Scope,
        };
    }

    private static void ValidateApplicationType(RegisterRequest request) {
        var applicationType = string.IsNullOrWhiteSpace(request.ApplicationType) ? ApplicationTypes.Web : request.ApplicationType;
        if (applicationType != ApplicationTypes.Web && applicationType != ApplicationTypes.Native) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_APPLICATION_TYPE_INVALID,
                Arg("value", applicationType));
        }
    }

    private static void ValidateRedirectUris(RegisterRequest request, SchemataAuthorizationOptions options) {
        if (request.RedirectUris is null || request.RedirectUris.Count == 0) {
            throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URIS_REQUIRED);
        }

        var applicationType = string.IsNullOrWhiteSpace(request.ApplicationType) ? ApplicationTypes.Web : request.ApplicationType;
        foreach (var uri in request.RedirectUris) {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || ((parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(parsed.Host))) {
                throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_NOT_ABSOLUTE,
                    Arg("value", uri));
            }

            // RFC 6749 §3.1.2: the endpoint URI MUST NOT include a fragment component.
            if (!string.IsNullOrEmpty(parsed.Fragment)) {
                throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_FRAGMENT,
                    Arg("value", uri));
            }

            // A redirect target with embedded credentials matches nothing at request time
            // (RedirectUriMatcher rejects userinfo), so registration refuses it up front.
            if (!string.IsNullOrEmpty(parsed.UserInfo)) {
                throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_USERINFO,
                    Arg("value", uri));
            }

            if (applicationType == ApplicationTypes.Web) {
                if (parsed.Scheme != Uri.UriSchemeHttps) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_HTTPS_REQUIRED,
                        Arg("value", uri));
                }

                if (IsLoopbackHost(parsed)) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_LOOPBACK_FORBIDDEN);
                }
            } else {
                // RFC 8252 §7: a native application registers private-use URI schemes, claimed
                // https redirect URIs (app links), or the http loopback IP literal - the three
                // legal profiles. localhost is not an additional requirement; any port is
                // matched at request time.
                if (parsed.Scheme == Uri.UriSchemeHttp && !RedirectUriMatcher.IsSupportedLoopback(uri, parsed)) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_LOOPBACK_REQUIRED,
                        Arg("value", uri));
                }

                if (options.RequireNativeSchemeDomain && parsed.Scheme != Uri.UriSchemeHttp
                    && parsed.Scheme != Uri.UriSchemeHttps && !parsed.Scheme.Contains('.')) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_METADATA_URI_INVALID,
                        new Dictionary<string, string?> { ["name"] = "redirect_uris", ["value"] = uri });
                }

                if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && parsed.Host == "localhost") {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, SchemataResources.REGISTRATION_REDIRECT_URI_LOCALHOST_FORBIDDEN,
                        Arg("value", uri));
                }
            }
        }
    }

    /// <summary>
    ///     Validates logout targets with field-specific transport policy and front-channel origin binding.
    /// </summary>
    private static void ValidateLogoutUris(RegisterRequest request, SchemataAuthorizationOptions options) {
        var native = request.ApplicationType == ApplicationTypes.Native;
        var confidential = !native && request.TokenEndpointAuthMethod != ClientAuthMethods.None;
        if (request.PostLogoutRedirectUris is { Count: > 0 }) {
            foreach (var uri in request.PostLogoutRedirectUris) {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !string.IsNullOrEmpty(parsed.Fragment)
                    || !string.IsNullOrEmpty(parsed.UserInfo)
                    || parsed.Scheme == Uri.UriSchemeHttp && (!confidential || !options.AllowHttpLogoutUris)
                    || parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && !native) {
                    throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_POST_LOGOUT_URI_INVALID,
                        Arg("value", uri));
                }
            }
        }

        foreach (var (value, name) in new (string?, string)[] {
                     (request.FrontChannelLogoutUri, "frontchannel_logout_uri"),
                     (request.BackChannelLogoutUri,  "backchannel_logout_uri"),
                 }) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                || parsed.Scheme != Uri.UriSchemeHttps && !(parsed.Scheme == Uri.UriSchemeHttp && confidential && options.AllowHttpLogoutUris)
                || !string.IsNullOrEmpty(parsed.Fragment) || !string.IsNullOrEmpty(parsed.UserInfo)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_METADATA_URI_INVALID,
                    new Dictionary<string, string?> {
                        ["name"]  = name,
                        ["value"] = value,
                    });
            }
            if (name == "frontchannel_logout_uri"
                && request.RedirectUris?.Any(redirect => Uri.TryCreate(redirect, UriKind.Absolute, out var target)
                    && target.Scheme == parsed.Scheme && target.Host == parsed.Host && target.Port == parsed.Port) != true) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_METADATA_URI_INVALID,
                    new Dictionary<string, string?> { ["name"] = name, ["value"] = value });
            }
        }
    }

    /// <summary>Collapses whitespace and drops empties so stored scope strings stay canonical.</summary>
    private static string? NormalizeScope(string? scope) {
        return string.IsNullOrWhiteSpace(scope)
            ? null
            : string.Join(' ', scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    ///     True when the request carries any signal of an interactive redirect profile: redirect
    ///     URIs, response types, an authorization_code/implicit grant, the openid scope, or an OIDC
    ///     subject/response field. A pure OAuth profile (e.g. client_credentials) answers false and
    ///     is not forced into redirect or code defaults.
    /// </summary>
    private static bool UsesRedirectFlow(RegisterRequest request) {
        if (request.RedirectUris is { Count: > 0 }
            || request.ResponseTypes is { Count: > 0 }
            || request.GrantTypes?.Any(g => g is GrantTypes.AuthorizationCode or GrantTypes.Implicit) == true) {
            return true;
        }

        if (request.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(Scopes.OpenId) == true) {
            return true;
        }

        // OIDC-only metadata selects the interactive profile on its own; the token-auth, key,
        // software, PAR, and JAR fields stay valid for pure OAuth registrations.
        return !string.IsNullOrWhiteSpace(request.SubjectType)
            || !string.IsNullOrWhiteSpace(request.SectorIdentifierUri)
            || !string.IsNullOrWhiteSpace(request.IdTokenSignedResponseAlg)
            || !string.IsNullOrWhiteSpace(request.UserinfoSignedResponseAlg)
            || !string.IsNullOrWhiteSpace(request.UserinfoEncryptedResponseAlg)
            || !string.IsNullOrWhiteSpace(request.UserinfoEncryptedResponseEnc)
            || request.DefaultMaxAge is not null
            || request.RequireAuthTime == true
            || request.DefaultAcrValues is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(request.InitiateLoginUri)
            || request.PostLogoutRedirectUris is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(request.FrontChannelLogoutUri)
            || request.FrontChannelLogoutSessionRequired == true
            || !string.IsNullOrWhiteSpace(request.BackChannelLogoutUri)
            || request.BackChannelLogoutSessionRequired == true;
    }

    private static (List<string>? GrantTypes, List<string>? ResponseTypes) NormalizeGrantAndResponseTypes(RegisterRequest request, bool redirectFlow) {
        // The OIDC DCR §2 defaults apply only to the redirect profile; an OAuth-only
        // registration keeps exactly the grants it declared and no response types.
        List<string>? grantTypes = request.GrantTypes is { Count: > 0 }
            ? request.GrantTypes
            : redirectFlow ? [GrantTypes.AuthorizationCode] : null;
        List<string>? responseTypes = request.ResponseTypes is { Count: > 0 }
            ? request.ResponseTypes.Select(SchemataApplicationMetadata.CanonicalizeResponseType).ToList()
            : redirectFlow ? [ResponseTypes.Code] : null;

        foreach (var responseType in responseTypes ?? []) {
            if (!ResponseTypeRequirements.TryGetValue(responseType, out var required)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_RESPONSE_TYPE_UNSUPPORTED,
                    Arg("value", responseType));
            }

            if (required.Any(r => grantTypes?.Contains(r) != true)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_RESPONSE_TYPE_GRANT_REQUIRED,
                    new Dictionary<string, string?> {
                        ["value"]    = responseType,
                        ["required"] = string.Join(", ", required),
                    });
            }
        }

        return (grantTypes, responseTypes);
    }

    private static void ValidateAuthMethod(RegisterRequest request, IOptions<SchemataAuthorizationOptions> options) {
        if (string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)) {
            return;
        }

        if (!options.Value.AllowedClientAuthMethods.Contains(request.TokenEndpointAuthMethod)) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_TOKEN_AUTH_METHOD_NOT_ALLOWED,
                Arg("value", request.TokenEndpointAuthMethod));
        }
        if (request.TokenEndpointAuthMethod == ClientAuthMethods.ClientSecretJwt
         && !string.IsNullOrWhiteSpace(request.TokenEndpointAuthSigningAlg)
         && !ClientAssertionAlgorithms.SymmetricAlgorithms.Contains(request.TokenEndpointAuthSigningAlg)) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }
    }

    private static void ValidateJwksPairing(RegisterRequest request) {
        if (request.Jwks is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null and not JsonValueKind.Object }) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_JWKS_NOT_OBJECT);
        }

        if (request.Jwks is { ValueKind: JsonValueKind.Object } && !string.IsNullOrWhiteSpace(request.JwksUri)) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_JWKS_EXCLUSIVE);
        }
        if (request.Jwks is { ValueKind: JsonValueKind.Object } jwks) ValidatePublicJwks(jwks);
    }

    private static void ValidatePublicJwks(JsonElement jwks) {
        if (jwks.ValueKind != JsonValueKind.Object || !jwks.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
        }
        foreach (var key in keys.EnumerateArray()) {
            if (key.ValueKind != JsonValueKind.Object
                || !key.TryGetProperty("kty", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() == "oct"
                || key.TryGetProperty("d", out _) || key.TryGetProperty("k", out _)
                || key.TryGetProperty("p", out _) || key.TryGetProperty("q", out _)
                || key.TryGetProperty("dp", out _) || key.TryGetProperty("dq", out _)
                || key.TryGetProperty("qi", out _) || key.TryGetProperty("oth", out _)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.INVALID_CLIENT_METADATA);
            }
        }
    }

    private static void ValidateDefaultMaxAge(RegisterRequest request) {
        // The runtime replays this value into the authorize request's max_age, an int of seconds.
        if (request.DefaultMaxAge is < 0 or > int.MaxValue) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_DEFAULT_MAX_AGE_INVALID,
                Arg("value", request.DefaultMaxAge.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static async Task ValidateSectorIdentifierAsync(RegisterRequest request, IHttpClientFactory http, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(request.SectorIdentifierUri)) {
            return;
        }

        if (request.RedirectUris is null) {
            return;
        }

        if (!Uri.TryCreate(request.SectorIdentifierUri, UriKind.Absolute, out var sector) || sector.Scheme != Uri.UriSchemeHttps) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_SECTOR_URI_INVALID);
        }

        using var client = http.CreateClient(nameof(RegistrationMetadataMapper));
        client.Timeout = TimeSpan.FromSeconds(10);

        string body;
        try {
            body = await client.GetStringAsync(sector, ct);
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_SECTOR_URI_FETCH_FAILED);
        }

        List<string>? sectorUris;
        try {
            sectorUris = JsonSerializer.Deserialize<List<string>>(body)?
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out _) ? u : null)
                .Where(u => u is not null)
                .Select(u => u!)
                .ToList();
        } catch (JsonException) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_SECTOR_URI_NOT_ARRAY);
        }

        if (sectorUris is null || sectorUris.Count == 0) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_SECTOR_URI_EMPTY);
        }

        // OIDC Registration §2: the sector document is a redirect_uri array; every registered
        // redirect URI must be a member of it by full string, not merely share a host.
        foreach (var uri in request.RedirectUris!) {
            if (!sectorUris.Contains(uri)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_SECTOR_URI_MEMBER_REQUIRED,
                    Arg("value", uri));
            }
        }
    }

    private static void ValidateUriHostConsistency(RegisterRequest request) {
        var hosts = request.RedirectUris!
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed.Host : null)
            .Where(h => !string.IsNullOrEmpty(h))
            .ToHashSet(StringComparer.Ordinal);

        var uriFields = new (string? Value, string Name)[] {
            (request.LogoUri,          "logo_uri"),
            (request.PolicyUri,        "policy_uri"),
            (request.TosUri,           "tos_uri"),
            (request.ClientUri,        "client_uri"),
            (request.InitiateLoginUri, "initiate_login_uri"),
        };

        foreach (var (value, name) in uriFields) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && !hosts.Contains(parsed.Host)) {
                // OIDC DCR §9.1 SHOULD: hosts should match redirect_uris; enforced by default.
                throw OAuthError(OAuthErrors.InvalidClientMetadata, SchemataResources.REGISTRATION_URI_HOST_MISMATCH,
                    Arg("name", name));
            }
        }
    }

    private static bool IsLoopbackHost(Uri uri) {
        return uri.Host is "127.0.0.1" or "[::1]" or "::1";
    }

    private static OAuthException OAuthError(
        string                                        error,
        string                                        resourceKey,
        IReadOnlyDictionary<string, string?>? args = null
    ) {
        return new(error, resourceKey, args);
    }

    private static Dictionary<string, string?> Arg(string key, string? value) {
        return new() { [key] = value };
    }

    internal static string Base64UrlEncode(byte[] bytes) {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateClientId() {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
    }
}
