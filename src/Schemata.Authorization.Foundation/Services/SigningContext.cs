using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     The signing selection for one issuance operation, resolved once by
///     <see cref="TokenService.BeginSigningAsync" /> and threaded through every token the
///     operation mints, so the JOSE signature and the hash claims bound to a token
///     (<c>at_hash</c>, <c>c_hash</c>, <c>ds_hash</c>) always derive from the same algorithm.
///     The context owns the key instances imported during resolution; dispose it when the
///     operation ends, after every token has been serialized.
/// </summary>
public sealed class SigningContext : IAsyncDisposable
{
    internal SigningContext(
        SigningCredentials         signing,
        SigningCredentials         primary,
        EncryptingCredentials?     encrypting,
        TokenValidationParameters  validation,
        IReadOnlyList<SecurityKey> ownedKeys
    ) {
        Signing    = signing;
        Primary    = primary;
        Encrypting = encrypting;
        Validation = validation;
        OwnedKeys  = ownedKeys;
    }

    /// <summary>
    ///     The negotiated signing credentials: the newest valid row serving the negotiated JWS
    ///     algorithm, or the issuer's primary row when no algorithm was negotiated.
    /// </summary>
    public SigningCredentials Signing { get; }

    /// <summary>
    ///     The issuer's primary signing credentials, taken from the newest valid signing row.
    ///     Access and refresh tokens sign with the primary selection; a client-registered
    ///     <c>id_token_signed_response_alg</c> governs ID tokens only (OIDC Registration §2).
    /// </summary>
    public SigningCredentials Primary { get; }

    /// <summary>The issuer's active encryption credentials, when a valid encryption row exists.</summary>
    public EncryptingCredentials? Encrypting { get; }

    /// <summary>The JWS algorithm of <see cref="Signing" />; hash-claim digest sizes follow it.</summary>
    public string Algorithm => Signing.Algorithm;

    internal TokenValidationParameters Validation { get; }

    private IReadOnlyList<SecurityKey> OwnedKeys { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        DisposeKeys(OwnedKeys);
        return ValueTask.CompletedTask;
    }

    internal static void DisposeKeys(IEnumerable<SecurityKey> keys) {
        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var key in keys) {
            switch (key) {
                case RsaSecurityKey { Rsa: { } rsa } when disposed.Add(rsa):
                    rsa.Dispose();
                    break;
                case ECDsaSecurityKey { ECDsa: { } ec } when disposed.Add(ec):
                    ec.Dispose();
                    break;
            }
        }
    }
}
