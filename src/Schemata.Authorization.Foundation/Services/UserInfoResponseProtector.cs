using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Signs and/or encrypts UserInfo responses with the unified token and secret
///     infrastructure, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfoResponse">
///         OpenID Connect Core 1.0 §5.3.2: Successful UserInfo Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     <para>
///         Signing uses the issuer's signing rows through <see cref="TokenService" /> (the
///         same keys and rotation semantics as every other token the server issues) and adds
///         the <c>iss</c> and <c>aud</c> claims §5.3.2 requires (issuer URL and the client
///         id). Each response uses the client's registered signing algorithm and a current
///         issuer key eligible for that algorithm.
///     </para>
///     <para>
///         Encryption uses the client's registered public key material — the same
///         <c>jwks</c>/<c>jwks-uri</c> security rows <c>private_key_jwt</c> verifies against
///         — so no second key channel exists. Signing and encryption together produce a
///         Nested JWT (signed then encrypted); encryption alone keeps the payload an
///         unsecured inner JWT, which §5.3.2 permits.
///     </para>
/// </remarks>
public sealed class UserInfoResponseProtector<TApp>(
    IApplicationManager<TApp>              apps,
    TokenService                           issuer,
    ISecurityStore<SchemataSecurity>       securities,
    IHttpClientFactory                     http,
    ICacheProvider                         cache,
    IOptions<SchemataSecurityOptions>      securityOptions,
    IOptions<SchemataAuthorizationOptions> options,
    TimeProvider?                          time = null
) : IUserInfoResponseProtector
    where TApp : SchemataApplication
{
    /// <summary>Lifetime stamped on a protected UserInfo JWT; §5.3.2 sets no exp requirement.</summary>
    public static readonly TimeSpan ResponseLifetime = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    #region IUserInfoResponseProtector Members

    public async Task<UserInfoJwt?> ProtectAsync(
        string?                             clientId,
        IReadOnlyDictionary<string, object> claims,
        CancellationToken                   ct = default
    ) {
        if (string.IsNullOrWhiteSpace(clientId)) {
            return null;
        }

        var app = await apps.FindByClientIdAsync(clientId, ct);
        if (app is null) {
            return null;
        }

        var signing   = !string.IsNullOrWhiteSpace(app.UserinfoSignedResponseAlg);
        var encrypted = !string.IsNullOrWhiteSpace(app.UserinfoEncryptedResponseAlg);

        if (!signing && !encrypted) {
            return null;
        }

        var payload = claims.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (signing) {
            payload[Claims.Issuer] = options.Value.Issuer!;
            payload[Claims.Audience] = app.ClientId!;
        }

        string response;

        if (signing) {
            var now = _time.GetUtcNow().UtcDateTime;
            await using var context = await issuer.BeginSigningAsync(app.UserinfoSignedResponseAlg, ct);
            var descriptor = new SecurityTokenDescriptor {
                Claims = payload,
                Issuer  = options.Value.Issuer,
                IssuedAt = now,
                Expires = now + ResponseLifetime,
                SigningCredentials = context.Signing,
            };

            if (encrypted) {
                // Core 1.0 §5.3.2: signed then encrypted — a Nested JWT.
                descriptor.EncryptingCredentials = await ResolveEncryptingCredentialsAsync(app, ct);
            }

            response = Handler.CreateToken(descriptor);
        } else {
            // Encrypted without a signature: the payload travels as an unsecured inner JWT.
            var now = _time.GetUtcNow().UtcDateTime;
            var descriptor = new SecurityTokenDescriptor {
                Claims = payload,
                IssuedAt = now,
                Expires = now + ResponseLifetime,
                EncryptingCredentials = await ResolveEncryptingCredentialsAsync(app, ct),
            };
            response = Handler.CreateToken(descriptor);
        }

        return new(response);
    }

    #endregion


    private async Task<EncryptingCredentials> ResolveEncryptingCredentialsAsync(TApp app, CancellationToken ct) {
        var parent = SecurityParents.Application(app);

        SchemataSecurity? row = null;
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
                "A client registering userinfo_encrypted_response_alg must register jwks or jwks_uri key material.");
        }

        var material = await row.ToKeyMaterialAsync(
            http.CreateClient(SecurityKeyMaterialExtensions.HttpClientName),
            cache,
            securityOptions.Value.KeyCacheLifetime,
            ct);

        var alg = SecurityKeyAdapter.ToEncryptionAlgorithm(app.UserinfoEncryptedResponseAlg!);
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
        if (key is null) {
            throw new InvalidOperationException("The client's registered key set has no eligible encryption key.", keyFailure);
        }

        var enc = string.IsNullOrWhiteSpace(app.UserinfoEncryptedResponseEnc)
            ? SecurityAlgorithms.Aes128CbcHmacSha256
            : app.UserinfoEncryptedResponseEnc!;

        return new(key, alg, enc);
    }
}
