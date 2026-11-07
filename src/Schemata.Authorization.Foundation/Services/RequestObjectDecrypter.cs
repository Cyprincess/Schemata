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
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Decrypts nested request objects with the issuer's current and retained encryption keys.</summary>
public sealed class RequestObjectDecrypter<TSecurity>(
    IOptions<JwtSecuredAuthorizationRequestsOptions> options,
    IOptions<SchemataAuthorizationOptions> server,
    IOptions<SchemataSecurityOptions> security,
    IHttpClientFactory http,
    ICacheProvider cache,
    ISecurityStore<TSecurity> securities,
    TimeProvider? time = null
) where TSecurity : SchemataSecurity, new()
{
    private static readonly CryptoProviderFactory NoProviderCache = new() { CacheSignatureProviders = false };
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<string> DecryptAsync(
        string assertion,
        SchemataApplication application,
        CancellationToken ct = default
    ) {
        var keys = new List<SecurityKey>();
        try {
            JsonWebToken jwt;
            try {
                jwt = new(assertion);
            } catch (Exception error) when (error is ArgumentException or SecurityTokenMalformedException) {
                throw InvalidRequestObject();
            }

            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedHeader));
            if (!TryReadHeader(header.RootElement, "alg", out var alg)
                || !TryReadHeader(header.RootElement, "enc", out var enc)
                || header.RootElement.TryGetProperty("cty", out var cty)
                && (cty.ValueKind != JsonValueKind.String
                    || !string.Equals(cty.GetString(), "JWT", StringComparison.OrdinalIgnoreCase))) {
                throw InvalidRequestObject();
            }

            if (!options.Value.EncryptionAlgorithms.Contains(alg)
                || !options.Value.ContentEncryptionAlgorithms.Contains(enc)
                || !string.IsNullOrWhiteSpace(application.RequestObjectEncryptionEnc)
                && string.IsNullOrWhiteSpace(application.RequestObjectEncryptionAlg)) {
                throw InvalidRequestObject();
            }

            if (!string.IsNullOrWhiteSpace(application.RequestObjectEncryptionAlg)) {
                var registeredEnc = application.RequestObjectEncryptionEnc
                    ?? ContentEncryptionAlgorithms.Aes128CbcHmacSha256;
                if (!string.Equals(application.RequestObjectEncryptionAlg, alg, StringComparison.Ordinal)
                    || !string.Equals(registeredEnc, enc, StringComparison.Ordinal)) {
                    throw InvalidRequestObject();
                }
            }

            var kid = header.RootElement.TryGetProperty("kid", out var keyId)
                && keyId.ValueKind == JsonValueKind.String
                ? keyId.GetString()
                : null;
            await LoadKeysAsync(alg, kid, true, keys, ct);
            if (keys.Count == 0) {
                throw InvalidRequestObject();
            }

            var signed = new JsonWebTokenHandler().DecryptToken(jwt, new() {
                TokenDecryptionKeys = keys,
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateLifetime = false,
            });
            if (signed.Count(character => character == '.') != 2) {
                throw InvalidRequestObject();
            }

            return signed;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (OAuthException) {
            throw;
        } catch (Exception error) when (error is ArgumentException
            or CryptographicException
            or FormatException
            or InvalidOperationException
            or JsonException
            or NotSupportedException
            or SecurityTokenException) {
            throw InvalidRequestObject();
        } finally {
            DisposeKeys(keys);
        }
    }

    public async Task<IReadOnlyList<string>> GetSupportedAlgorithmsAsync(CancellationToken ct = default) {
        var supported = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<SecurityKey>();
        try {
            foreach (var algorithm in options.Value.EncryptionAlgorithms) {
                var before = keys.Count;
                await LoadKeysAsync(algorithm, null, false, keys, ct);
                if (keys.Count > before) {
                    supported.Add(algorithm);
                }
                DisposeKeys(keys);
                keys.Clear();
            }
        } finally {
            DisposeKeys(keys);
        }

        return supported.OrderBy(value => value, StringComparer.Ordinal).ToList();
    }

    private async Task LoadKeysAsync(
        string algorithm,
        string? kid,
        bool includeRetained,
        List<SecurityKey> keys,
        CancellationToken ct
    ) {
        var issuer = server.Value.Issuer ?? throw new InvalidOperationException("The authorization issuer is not configured.");
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(issuer), null, SecurityConstants.Usages.Encryption, null, ct)) {
            if (row.Status != SecurityConstants.Statuses.Valid
                && (!includeRetained
                    || !SecurityKeyLifecycle.IsRetained(row, _time.GetUtcNow(), security.Value.RetiredKeyRetention))
                || !string.Equals(SecurityKeyAdapter.ToEncryptionAlgorithm(row.Algorithm), algorithm, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(kid) && !string.Equals(row.Kid, kid, StringComparison.Ordinal)) {
                continue;
            }

            var material = await row.ToKeyMaterialAsync(
                http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName),
                cache,
                security.Value.KeyCacheLifetime,
                ct);
            if (material is null || SecurityKeyAdapter.ToSecurityKey(material) is not { } key) {
                continue;
            }

            key.CryptoProviderFactory = NoProviderCache;
            if (!key.CryptoProviderFactory.IsSupportedAlgorithm(algorithm, key)) {
                DisposeKey(key);
                continue;
            }
            keys.Add(key);
        }
    }

    private static bool TryReadHeader(JsonElement header, string name, out string value) {
        if (header.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } text) {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void DisposeKeys(IEnumerable<SecurityKey> keys) {
        foreach (var key in keys) {
            DisposeKey(key);
        }
    }

    private static void DisposeKey(SecurityKey key) {
        switch (key) {
            case RsaSecurityKey { Rsa: { } rsa }:
                rsa.Dispose();
                break;
            case ECDsaSecurityKey { ECDsa: { } ec }:
                ec.Dispose();
                break;
        }
    }

    private static OAuthException InvalidRequestObject() {
        return new(OAuthErrors.InvalidRequestObject, SchemataResources.INVALID_REQUEST_OBJECT);
    }
}
