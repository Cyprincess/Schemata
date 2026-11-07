using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Selects a recipient public key from the client's registered key material.</summary>
public sealed class ClientEncryptionKeyResolver<TSecurity>(
    ISecurityStore<TSecurity>         securities,
    IHttpClientFactory                http,
    ICacheProvider                    cache,
    IOptions<SchemataSecurityOptions> options
) where TSecurity : SchemataSecurity, new()
{
    public async Task<EncryptingCredentials> ResolveAsync(
        SchemataApplication application,
        string              algorithm,
        string?             contentAlgorithm,
        CancellationToken   ct = default
    ) {
        var parent = SecurityParents.Application(application);

        TSecurity? row = null;
        await foreach (var candidate in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, SecurityConstants.Statuses.Valid, ct)) {
            row = candidate;
            break;
        }

        if (row is null) {
            await foreach (var candidate in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, SecurityConstants.Statuses.Valid, ct)) {
                row = candidate;
                break;
            }
        }

        if (row is null) {
            throw new InvalidOperationException(
                "A client registering an encrypted response algorithm must register jwks or jwks_uri key material.");
        }

        var material = await row.ToKeyMaterialAsync(
            http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName),
            cache,
            options.Value.KeyCacheLifetime,
            ct);

        var alg = SecurityKeyAdapter.ToEncryptionAlgorithm(algorithm);
        SecurityKey? key = null;
        Exception? keyFailure = null;
        if (material is not null) {
            foreach (var candidate in SecurityKeyAdapter.ToJsonWebKeySet([material]).Keys) {
                if (!string.IsNullOrEmpty(candidate.Use) && candidate.Use != "enc"
                 || !string.IsNullOrEmpty(candidate.Alg) && candidate.Alg != alg
                 || candidate.KeyOps.Count > 0 && !candidate.KeyOps.Contains("encrypt") && !candidate.KeyOps.Contains("wrapKey")) {
                    continue;
                }
                if (!candidate.CryptoProviderFactory.IsSupportedAlgorithm(alg, candidate)) continue;
                try {
                    var provider = candidate.CryptoProviderFactory.CreateKeyWrapProvider(candidate, alg);
                    candidate.CryptoProviderFactory.ReleaseKeyWrapProvider(provider);
                    key = candidate;
                    break;
                } catch (Exception error) when (error is ArgumentException or CryptographicException or NotSupportedException) {
                    keyFailure = error;
                }
            }
        }
        if (key is null || alg is null) {
            throw new InvalidOperationException("The client's registered key set has no eligible encryption key.", keyFailure);
        }

        var enc = string.IsNullOrWhiteSpace(contentAlgorithm)
            ? SecurityAlgorithms.Aes128CbcHmacSha256
            : contentAlgorithm;

        return new(key, alg, enc);
    }
}
