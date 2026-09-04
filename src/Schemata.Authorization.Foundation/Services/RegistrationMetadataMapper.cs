using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
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

    /// <summary>Validates the request, mints the client identifier, maps the request onto a new
    /// application entity, and stores <c>jwks</c> / <c>jwks_uri</c> material as security rows.</summary>
    public static async Task<TApp> ToApplicationAsync<TApp>(
        RegisterRequest                        request,
        IOptions<SchemataAuthorizationOptions> options,
        IHttpClientFactory                     http,
        ISecurityStore<SchemataSecurity>       securities,
        TimeProvider?                          time = null,
        CancellationToken                      ct   = default
    )
        where TApp : SchemataApplication, new()
    {
        ValidateRedirectUris(request);
        ValidateAuthMethod(request, options);
        ValidateJwksPairing(request);
        await ValidateSectorIdentifierAsync(request, http, ct);
        ValidateUriHostConsistency(request);

        var (grantTypes, responseTypes) = NormalizeGrantAndResponseTypes(request);

        var applicationType = string.IsNullOrWhiteSpace(request.ApplicationType) ? ApplicationTypes.Web : request.ApplicationType;

        var application = new TApp {
            ClientId                            = GenerateClientId(),
            ClientType                          = IsPublicAuthMethod(request.TokenEndpointAuthMethod) ? ClientTypes.Public : ClientTypes.Confidential,
            ApplicationType                     = applicationType,
            RedirectUris                        = request.RedirectUris,
            PostLogoutRedirectUris              = request.PostLogoutRedirectUris,
            ClientName                          = request.ClientName,
            Contacts                            = request.Contacts,
            ClientUri                           = request.ClientUri,
            LogoUri                             = request.LogoUri,
            PolicyUri                           = request.PolicyUri,
            TosUri                              = request.TosUri,
            TokenEndpointAuthMethod             = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
                                                      ? ClientAuthMethods.ClientSecretBasic
                                                      : request.TokenEndpointAuthMethod,
            TokenEndpointAuthSigningAlg         = request.TokenEndpointAuthSigningAlg,
            SubjectType                         = request.SubjectType,
            SectorIdentifierUri                 = request.SectorIdentifierUri,
            DefaultMaxAge                       = request.DefaultMaxAge,
            RequireAuthTime                     = request.RequireAuthTime ?? false,
            DefaultAcrValues                    = request.DefaultAcrValues,
            InitiateLoginUri                    = request.InitiateLoginUri,
            FrontChannelLogoutUri               = request.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired   = request.FrontChannelLogoutSessionRequired ?? false,
            BackChannelLogoutUri                = request.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired    = request.BackChannelLogoutSessionRequired ?? false,
            SoftwareId                          = request.SoftwareId,
            SoftwareVersion                     = request.SoftwareVersion,
            SoftwareStatement                   = request.SoftwareStatement,
            Permissions                         = BuildPermissions(request, grantTypes, responseTypes, options),
        };

        if (!string.IsNullOrWhiteSpace(request.Jwks)) {
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(application),
                Name   = application.ClientId,
                Kind   = SecurityConstants.Kinds.Jwks,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = request.Jwks,
                Status = SecurityConstants.Statuses.Valid,
            }, ct);
        }

        if (!string.IsNullOrWhiteSpace(request.JwksUri)) {
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(application),
                Name   = application.ClientId,
                Kind   = SecurityConstants.Kinds.JwksUri,
                Usage  = SecurityConstants.Usages.Authentication,
                Value  = request.JwksUri,
                Status = SecurityConstants.Statuses.Valid,
            }, ct);
        }

        return application;
    }

    /// <summary>Maps a stored application back onto the wire response shape, echoing the
    /// client's newest <c>jwks</c> / <c>jwks_uri</c> security rows.</summary>
    public static async Task<RegistrationResponse> ToResponse(
        SchemataApplication              application,
        ISecurityStore<SchemataSecurity> securities,
        CancellationToken                ct = default
    ) {
        var parent = SecurityParents.Application(application);

        string? jwks = null;
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, null, ct)) {
            jwks = row.Value;
            break;
        }

        string? jwksUri = null;
        await foreach (var row in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, null, ct)) {
            jwksUri = row.Value;
            break;
        }

        return new() {
            ClientId                            = application.ClientId,
            RedirectUris                        = application.RedirectUris?.ToList(),
            PostLogoutRedirectUris              = application.PostLogoutRedirectUris?.ToList(),
            ClientName                          = application.ClientName,
            Contacts                            = application.Contacts?.ToList(),
            ClientUri                           = application.ClientUri,
            LogoUri                             = application.LogoUri,
            PolicyUri                           = application.PolicyUri,
            TosUri                              = application.TosUri,
            Jwks                                = jwks,
            JwksUri                             = jwksUri,
            TokenEndpointAuthMethod             = application.TokenEndpointAuthMethod,
            TokenEndpointAuthSigningAlg         = application.TokenEndpointAuthSigningAlg,
            ApplicationType                     = application.ApplicationType,
            SubjectType                         = application.SubjectType,
            SectorIdentifierUri                 = application.SectorIdentifierUri,
            DefaultMaxAge                       = application.DefaultMaxAge,
            RequireAuthTime                     = application.RequireAuthTime,
            DefaultAcrValues                    = application.DefaultAcrValues?.ToList(),
            InitiateLoginUri                    = application.InitiateLoginUri,
            FrontChannelLogoutUri               = application.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired   = application.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri                = application.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired    = application.BackChannelLogoutSessionRequired,
            SoftwareId                          = application.SoftwareId,
            SoftwareVersion                     = application.SoftwareVersion,
            GrantTypes                          = application.Permissions?
                                                      .Where(p => p.StartsWith(PermissionPrefixes.GrantType, StringComparison.Ordinal))
                                                      .Select(p => p[PermissionPrefixes.GrantType.Length..])
                                                      .ToList(),
            ResponseTypes                       = application.Permissions?
                                                      .Where(p => p.StartsWith(PermissionPrefixes.ResponseType, StringComparison.Ordinal))
                                                      .Select(p => p[PermissionPrefixes.ResponseType.Length..])
                                                      .ToList(),
            Scope                               = application.Permissions?
                                                      .Where(p => p.StartsWith(PermissionPrefixes.Scope, StringComparison.Ordinal))
                                                      .Select(p => p[PermissionPrefixes.Scope.Length..])
                                                      .ToList() is { Count: > 0 } scopes ? string.Join(' ', scopes) : null,
        };
    }

    private static void ValidateRedirectUris(RegisterRequest request) {
        if (request.RedirectUris is null || request.RedirectUris.Count == 0) {
            throw OAuthError(OAuthErrors.InvalidRedirectUri, "redirect_uris is required.");
        }

        var applicationType = string.IsNullOrWhiteSpace(request.ApplicationType) ? ApplicationTypes.Web : request.ApplicationType;
        if (applicationType != ApplicationTypes.Web && applicationType != ApplicationTypes.Native) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, $"application_type must be web or native, not {applicationType}.");
        }

        foreach (var uri in request.RedirectUris) {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || ((parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(parsed.Host))) {
                throw OAuthError(OAuthErrors.InvalidRedirectUri, $"redirect_uris entry is not an absolute URI: {uri}");
            }

            if (applicationType == ApplicationTypes.Web) {
                if (parsed.Scheme != Uri.UriSchemeHttps) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, $"web clients must register https redirect URIs: {uri}");
                }

                if (IsLoopbackHost(parsed)) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, "web clients must not register loopback redirect URIs.");
                }
            } else {
                // native: custom scheme, or http on loopback IP literals (localhost excluded per RFC 8252 §8.3 tightening)
                if (parsed.Scheme == Uri.UriSchemeHttp) {
                    if (!IsLoopbackHost(parsed) || parsed.Host == "localhost") {
                        throw OAuthError(OAuthErrors.InvalidRedirectUri, $"native http redirect URIs must use loopback IP literals: {uri}");
                    }
                } else if (parsed.Scheme == Uri.UriSchemeHttps) {
                    throw OAuthError(OAuthErrors.InvalidRedirectUri, $"native clients must use custom schemes or loopback http: {uri}");
                }
            }
        }
    }

    private static (List<string> GrantTypes, List<string> ResponseTypes) NormalizeGrantAndResponseTypes(RegisterRequest request) {
        var grantTypes    = request.GrantTypes is { Count: > 0 } ? request.GrantTypes : [GrantTypes.AuthorizationCode];
        var responseTypes = request.ResponseTypes is { Count: > 0 } ? request.ResponseTypes : [ResponseTypes.Code];

        foreach (var responseType in responseTypes) {
            if (!ResponseTypeRequirements.TryGetValue(responseType, out var required)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata, $"unsupported response_type: {responseType}");
            }

            if (required.Any(r => !grantTypes.Contains(r))) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata,
                    $"response_type {responseType} requires grant_types {string.Join(", ", required)}.");
            }
        }

        return (grantTypes, responseTypes);
    }

    private static void ValidateAuthMethod(RegisterRequest request, IOptions<SchemataAuthorizationOptions> options) {
        if (string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)) {
            return;
        }

        if (!options.Value.AllowedClientAuthMethods.Contains(request.TokenEndpointAuthMethod)) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata,
                $"token_endpoint_auth_method {request.TokenEndpointAuthMethod} is not among the server's allowed methods.");
        }
    }

    private static void ValidateJwksPairing(RegisterRequest request) {
        if (!string.IsNullOrWhiteSpace(request.Jwks) && !string.IsNullOrWhiteSpace(request.JwksUri)) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, "jwks and jwks_uri are mutually exclusive.");
        }
    }

    private static async Task ValidateSectorIdentifierAsync(RegisterRequest request, IHttpClientFactory http, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(request.SectorIdentifierUri)) {
            return;
        }

        if (!Uri.TryCreate(request.SectorIdentifierUri, UriKind.Absolute, out var sector) || sector.Scheme != Uri.UriSchemeHttps) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, "sector_identifier_uri must be an absolute https URI.");
        }

        using var client = http.CreateClient(nameof(RegistrationMetadataMapper));
        client.Timeout = TimeSpan.FromSeconds(10);

        string body;
        try {
            body = await client.GetStringAsync(sector, ct);
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, "sector_identifier_uri could not be fetched.");
        }

        List<string>? sectorHosts;
        try {
            sectorHosts = JsonSerializer.Deserialize<List<string>>(body)?
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed.Host : null)
                .Where(h => h is not null)
                .Select(h => h!)
                .ToList();
        } catch (JsonException) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, "sector_identifier_uri returned a non-array document.");
        }

        if (sectorHosts is null || sectorHosts.Count == 0) {
            throw OAuthError(OAuthErrors.InvalidClientMetadata, "sector_identifier_uri returned an empty redirect_uris array.");
        }

        foreach (var uri in request.RedirectUris!) {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !sectorHosts.Contains(parsed.Host)) {
                throw OAuthError(OAuthErrors.InvalidClientMetadata,
                    $"redirect URI host not covered by sector_identifier_uri: {uri}");
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
                throw OAuthError(OAuthErrors.InvalidClientMetadata, $"{name} host does not match any redirect_uris host.");
            }
        }
    }

    private static List<string> BuildPermissions(
        RegisterRequest                        request,
        List<string>                           grantTypes,
        List<string>                           responseTypes,
        IOptions<SchemataAuthorizationOptions> options
    ) {
        var permissions = new List<string>();
        permissions.AddRange(grantTypes.Select(g => $"{PermissionPrefixes.GrantType}{g}"));
        permissions.AddRange(responseTypes.Select(r => $"{PermissionPrefixes.ResponseType}{r}"));

        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            permissions.AddRange(request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => $"{PermissionPrefixes.Scope}{s}"));
        }

        return permissions;
    }

    private static bool IsPublicAuthMethod(string? method) {
        return method == ClientAuthMethods.None;
    }

    private static bool IsLoopbackHost(Uri uri) {
        return uri.Host is "127.0.0.1" or "[::1]" or "::1";
    }

    private static OAuthException OAuthError(string error, string description) {
        return new(error, description);
    }

    internal static string Base64UrlEncode(byte[] bytes) {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateClientId() {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
    }
}
