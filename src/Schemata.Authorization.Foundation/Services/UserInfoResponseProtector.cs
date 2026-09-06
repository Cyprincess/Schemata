using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
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
///         id). The per-client <c>userinfo_signed_response_alg</c> must equal the issuer's
///         active signing algorithm — dynamic registration rejects any other value at
///         registration time, so the metadata and the minted header cannot drift.
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

        var list = new List<Claim>();

        // §5.3.2: a signed response MUST contain iss (the OP's issuer) and aud (the client id).
        if (signing) {
            list.Add(new(Claims.Issuer, options.Value.Issuer!));
            list.Add(new(Claims.Audience, app.ClientId!));
        }

        foreach (var (name, value) in claims) {
            switch (value) {
                case string scalar:
                    list.Add(new(name, scalar));
                    break;
                case IEnumerable<string> multi:
                    list.AddRange(multi.Select(v => new Claim(name, v)));
                    break;
                default:
                    list.Add(new(name, value.ToString() ?? string.Empty));
                    break;
            }
        }

        string response;

        if (signing) {
            var now = _time.GetUtcNow().UtcDateTime;
            var descriptor = new SecurityTokenDescriptor {
                Subject = new(list),
                Issuer  = options.Value.Issuer,
                IssuedAt = now,
                Expires = now + ResponseLifetime,
                // The issuer's primary signing row signs; the registered alg was validated
                // to match it at registration time.
                SigningCredentials = await issuer.ResolveSigningCredentials(ct),
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
                Subject = new(list),
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
        await foreach (var candidate in securities.ListByParentAsync(parent, SecurityConstants.Kinds.Jwks, null, null, ct)) {
            row = candidate;
            break;
        }

        if (row is null) {
            await foreach (var candidate in securities.ListByParentAsync(parent, SecurityConstants.Kinds.JwksUri, null, null, ct)) {
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

        var key = material is null ? null : SecurityKeyAdapter.ToSecurityKey(material);
        if (key is null) {
            throw new InvalidOperationException("The client's registered key material could not be loaded.");
        }

        var alg = SecurityKeyAdapter.ToEncryptionAlgorithm(app.UserinfoEncryptedResponseAlg!);
        var enc = string.IsNullOrWhiteSpace(app.UserinfoEncryptedResponseEnc)
            ? options.Value.ContentEncryptionAlgorithm
            : app.UserinfoEncryptedResponseEnc!;

        return new(key, alg, enc);
    }
}
