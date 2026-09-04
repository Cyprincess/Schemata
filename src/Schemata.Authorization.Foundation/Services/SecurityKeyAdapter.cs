using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Skeleton;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Adapts stored security material (<see cref="SchemataKeyMaterial" />) to JOSE key types
///     (<see cref="SecurityKey" />, <see cref="JsonWebKeySet" />): a static, query-free type
///     conversion — no queries, aggregation, or network. Rows locating material by URI must be
///     resolved through <c>ToKeyMaterialAsync</c> first. The key-algorithm → JWS/JWE mapping
///     (<see cref="ToSigningAlgorithm" /> / <see cref="ToEncryptionAlgorithm" />) lives here
///     too; the token pipeline selects the credentials.
/// </summary>
public static class SecurityKeyAdapter
{
    /// <summary>Adapts single-key material to a <see cref="SecurityKey" />, stamped with the row's Kid.</summary>
    /// <returns>The adapted key, or <see langword="null" /> when the material publishes through a key
    /// set (jwk / jwks rows carry their own JSON and kids); use <see cref="ToJsonWebKeySet" />.</returns>
    /// <exception cref="InvalidOperationException">The material locates its key by URI
    /// (jwks-uri / public-key-uri); resolve it via <c>ToKeyMaterialAsync</c> before adapting.</exception>
    public static SecurityKey? ToSecurityKey(SchemataKeyMaterial material) {
        ArgumentNullException.ThrowIfNull(material);

        return material.Material switch {
            SecurityKeyMaterial.RsaKey    rsa => Stamp(new RsaSecurityKey(rsa.Key), material.Security),
            SecurityKeyMaterial.EcKey     ec  => Stamp(new ECDsaSecurityKey(ec.Key), material.Security),
            SecurityKeyMaterial.Symmetric sym => Stamp(new SymmetricSecurityKey(sym.Key), material.Security),
            SecurityKeyMaterial.PublicKeyPem pem => ImportPem(pem.Pem, material.Security),
            SecurityKeyMaterial.JwkJson or SecurityKeyMaterial.JwksJson => null,
            SecurityKeyMaterial.KeyUri => throw new InvalidOperationException(
                "URI-located key material must be resolved via ToKeyMaterialAsync before adaptation."),
            _ => null,
        };
    }

    /// <summary>Builds a key set from material rows. jwk / jwks rows parse their own JSON and keep
    /// their own kids; other rows convert through <see cref="ToSecurityKey" />.</summary>
    /// <exception cref="InvalidOperationException">A row locates its key by URI; resolve it via
    /// <c>ToKeyMaterialAsync</c> before adapting.</exception>
    public static JsonWebKeySet ToJsonWebKeySet(IReadOnlyList<SchemataKeyMaterial> materials) {
        ArgumentNullException.ThrowIfNull(materials);

        var set = new JsonWebKeySet();

        foreach (var material in materials) {
            switch (material.Material) {
                case SecurityKeyMaterial.JwkJson jwk:
                    set.Keys.Add(new(jwk.Json));
                    break;

                case SecurityKeyMaterial.JwksJson jwks:
                    foreach (var parsed in new JsonWebKeySet(jwks.Json).Keys) {
                        set.Keys.Add(parsed);
                    }

                    break;

                default:
                    if (ToJsonWebKey(ToSecurityKey(material)) is { } key) {
                        set.Keys.Add(key);
                    }

                    break;
            }
        }

        return set;
    }

    /// <summary>Maps a row's key algorithm to the JWS signing algorithm used for signing
    /// credentials and published metadata: rsa → RS256, p-256 / p-384 / p-521 →
    /// ES256 / ES384 / ES512. Any other value passes through unchanged — jwk rows may carry a
    /// JWS algorithm directly.</summary>
    public static string? ToSigningAlgorithm(string? algorithm) {
        return algorithm switch {
            SecurityConstants.Algorithms.Rsa  => AuthorizationConstants.SigningAlgorithms.RsaSha256,
            SecurityConstants.Algorithms.P256 => AuthorizationConstants.SigningAlgorithms.EcdsaSha256,
            SecurityConstants.Algorithms.P384 => AuthorizationConstants.SigningAlgorithms.EcdsaSha384,
            SecurityConstants.Algorithms.P521 => AuthorizationConstants.SigningAlgorithms.EcdsaSha512,
            _                                 => algorithm,
        };
    }

    /// <summary>Maps a row's key algorithm to the JWE key-management algorithm used for
    /// encryption credentials: rsa → RSA-OAEP, p-256 / p-384 / p-521 → ECDH-ES. Any other
    /// value passes through unchanged.</summary>
    public static string? ToEncryptionAlgorithm(string? algorithm) {
        return algorithm switch {
            SecurityConstants.Algorithms.Rsa  => AuthorizationConstants.EncryptionAlgorithms.RsaOaep,
            SecurityConstants.Algorithms.P256 => AuthorizationConstants.EncryptionAlgorithms.EcdhEs,
            SecurityConstants.Algorithms.P384 => AuthorizationConstants.EncryptionAlgorithms.EcdhEs,
            SecurityConstants.Algorithms.P521 => AuthorizationConstants.EncryptionAlgorithms.EcdhEs,
            _                                 => algorithm,
        };
    }

    private static SecurityKey Stamp(SecurityKey key, SchemataSecurity security) {
        key.KeyId = security.Kid;
        return key;
    }

    private static JsonWebKey? ToJsonWebKey(SecurityKey? key) {
        return key switch {
            RsaSecurityKey or ECDsaSecurityKey or SymmetricSecurityKey
                => JsonWebKeyConverter.ConvertFromSecurityKey(key),
            _ => null,
        };
    }

    /// <summary>Imports a public PEM as RSA, then EC; returns <see langword="null" /> when the PEM
    /// is neither. The imported instance is owned by the holder of the returned key.</summary>
    private static SecurityKey? ImportPem(string pem, SchemataSecurity security) {
        var rsa = RSA.Create();
        try {
            rsa.ImportFromPem(pem);
            return Stamp(new RsaSecurityKey(rsa), security);
        } catch (Exception ex) when (ex is CryptographicException or FormatException) {
            rsa.Dispose();
        }

        var ec = ECDsa.Create();
        try {
            ec.ImportFromPem(pem);
            return Stamp(new ECDsaSecurityKey(ec), security);
        } catch (Exception ex) when (ex is CryptographicException or FormatException) {
            ec.Dispose();
        }

        return null;
    }
}
