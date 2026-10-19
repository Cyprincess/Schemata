using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Foundation.Services;

/// <summary>The single construction path for key material from stored security rows.</summary>
public static class SecurityKeyMaterialExtensions
{
    /// <summary>The named <see cref="HttpClient" /> registered for URI material fetches; it carries
    /// the caller-configured timeout.</summary>
    public const string HttpClientName = "SchemataSecurityKeys";

    /// <summary>Loads key material from a stored security row. <c>secret</c> rows yield UTF-8
    /// <see cref="SecurityKeyMaterial.Symmetric" /> bytes; <c>public-key</c> rows stay
    /// <see cref="SecurityKeyMaterial.PublicKeyPem" />; <c>jwk</c> / <c>jwks</c> rows pass their
    /// JSON through as <see cref="SecurityKeyMaterial.JwkJson" /> /
    /// <see cref="SecurityKeyMaterial.JwksJson" />; URI rows (<c>jwks-uri</c> /
    /// <c>public-key-uri</c>) resolve cache-first: a hit under the Authorization-domain key
    /// <c>security-keys\x1e{CanonicalName ?? Uid}</c> returns the cached document, a miss fetches
    /// <see cref="SchemataSecurity.Value" /> through <paramref name="http" />, caches the body for
    /// <paramref name="ttl" />, and returns it as <see cref="SecurityKeyMaterial.JwksJson" />.
    /// <c>certificate</c> and unrecognized kinds yield <see langword="null" />.</summary>
    /// <param name="security">The stored row.</param>
    /// <param name="http">Client performing URI fetches; use the named
    /// <see cref="HttpClientName" /> client or an equivalent with a configured timeout.</param>
    /// <param name="cache">Cache holding fetched URI documents.</param>
    /// <param name="ttl">Lifetime of fetched URI documents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded material, or <see langword="null" /> when the row carries no loadable
    /// material or a blank <see cref="SchemataSecurity.Value" />.</returns>
    /// <exception cref="NotSupportedException">A private-key row declares no key algorithm, an
    /// algorithm other than <c>rsa</c> / <c>p-256</c> / <c>p-384</c> / <c>p-521</c> (none of which
    /// can come from a PEM — x25519 and ed25519 belong on dedicated kinds), or a value the
    /// declared algorithm cannot import. JWK-encoded private keys belong on <c>jwk</c> rows;
    /// a private-key row that fails its declared import is a data-integrity failure and throws.</exception>
    /// <remarks>Asymmetric keys are freshly imported on every call; the caller owns the returned
    /// <see cref="RSA" /> / <see cref="ECDsa" /> instances and must dispose them. No instances
    /// are shared or cached.</remarks>
    public static async Task<SchemataKeyMaterial?> ToKeyMaterialAsync<TSecurity>(
        this TSecurity    security,
        HttpClient        http,
        ICacheProvider    cache,
        TimeSpan          ttl,
        CancellationToken ct = default
    ) where TSecurity : SchemataSecurity {
        ArgumentNullException.ThrowIfNull(security);

        if (string.IsNullOrWhiteSpace(security.Value)) {
            return null;
        }

        if (security.Kind is Kinds.JwksUri or Kinds.PublicKeyUri) {
            return new(security, new SecurityKeyMaterial.JwksJson(await FetchAsync(security, http, cache, ttl, ct)));
        }

        SecurityKeyMaterial? material = security.Kind switch {
            Kinds.Secret     => new SecurityKeyMaterial.Symmetric(Encoding.UTF8.GetBytes(security.Value)),
            Kinds.PrivateKey => ImportPrivateKey(security),
            Kinds.PublicKey  => new SecurityKeyMaterial.PublicKeyPem(security.Value),
            Kinds.Jwk        => new SecurityKeyMaterial.JwkJson(security.Value),
            Kinds.Jwks       => new SecurityKeyMaterial.JwksJson(security.Value),
            _                => null,
        };

        return material is null ? null : new(security, material);
    }

    private static async Task<string> FetchAsync<TSecurity>(
        TSecurity         security,
        HttpClient        http,
        ICacheProvider    cache,
        TimeSpan          ttl,
        CancellationToken ct
    ) where TSecurity : SchemataSecurity {
        var key = $"security-keys\x1e{security.CanonicalName ?? security.Uid.ToString()}".ToCacheKey(Keys.Authorization);

        var cached = await cache.GetAsync(key, ct);
        if (cached is not null) {
            return Encoding.UTF8.GetString(cached);
        }

        var json = await http.GetStringAsync(security.Value, ct);
        await cache.SetAsync(key, Encoding.UTF8.GetBytes(json), new() {
            AbsoluteExpirationRelativeToNow = ttl,
        }, ct);

        return json;
    }

    private static SecurityKeyMaterial ImportPrivateKey<TSecurity>(TSecurity security)
        where TSecurity : SchemataSecurity
    {
        var algorithm = security.Algorithm;
        if (algorithm is not (Algorithms.Rsa or Algorithms.P256 or Algorithms.P384 or Algorithms.P521)) {
            throw new NotSupportedException(
                $"The private-key row '{Describe(security)}' declares the algorithm '{algorithm ?? "<null>"}'; " +
                "a PEM private key requires one of rsa, p-256, p-384, or p-521.");
        }

        static string NotImportable(SchemataSecurity row, string declared) {
            return $"The private-key row '{Describe(row)}' declares the algorithm '{declared}', " +
                   "but its value is not a PEM the declared algorithm can import.";
        }

        if (algorithm == Algorithms.Rsa) {
            var rsa = RSA.Create();
            try {
                rsa.ImportFromPem(security.Value);
                return new SecurityKeyMaterial.RsaKey(rsa);
            } catch (Exception ex) {
                rsa.Dispose();
                throw new NotSupportedException(NotImportable(security, algorithm), ex);
            }
        }

        var ec = ECDsa.Create();
        try {
            ec.ImportFromPem(security.Value);
            return new SecurityKeyMaterial.EcKey(ec);
        } catch (Exception ex) {
            ec.Dispose();
            throw new NotSupportedException(NotImportable(security, algorithm), ex);
        }
    }

    private static string Describe(SchemataSecurity security) {
        return security.Name ?? security.Uid.ToString();
    }
}
