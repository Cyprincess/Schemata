using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Seeds ephemeral issuer key rows into a security store, standing in for the
///     provisioning the removed options helpers used to perform.
/// </summary>
public static class TestSecurityKeys
{
    public static async Task<string> CreateToken(
        this TokenService                         service,
        IEnumerable<System.Security.Claims.Claim> claims,
        TimeSpan                                  lifetime,
        bool                                      encrypt = false,
        string?                                   typ     = null
    ) {
        var         issuedAt = DateTimeOffset.UtcNow;
        await using var signing = await service.BeginSigningAsync();
        return service.CreateToken(signing, signing.Signing, claims, issuedAt, issuedAt + lifetime, encrypt, typ);
    }

    /// <summary>Adds a valid private-key signing row under the issuer; mints a key matching
    /// <paramref name="algorithm" /> when <paramref name="key" /> is null.</summary>
    /// <param name="store">Security store receiving the row.</param>
    /// <param name="issuer">Issuer URI the row belongs to.</param>
    /// <param name="algorithm">Key algorithm stored on the row (rsa / p-256 / …); the JWS
    /// algorithm derives from it at the token pipeline.</param>
    /// <param name="key">Optional asymmetric key; exported as PKCS#8 PEM and not retained.</param>
    /// <returns>The created row.</returns>
    public static SchemataSecurity AddSigningRow(
        ISecurityStore<SchemataSecurity> store,
        string                           issuer,
        string                           algorithm = SecurityConstants.Algorithms.Rsa,
        AsymmetricAlgorithm?             key       = null
    ) {
        return AddRow(store, issuer, SecurityConstants.Usages.Signing, algorithm, key);
    }

    /// <summary>Adds a valid private-key encryption row under the issuer; twin of
    /// <see cref="AddSigningRow" />.</summary>
    /// <param name="store">Security store receiving the row.</param>
    /// <param name="issuer">Issuer URI the row belongs to.</param>
    /// <param name="algorithm">Key algorithm stored on the row (rsa / p-256 / …); the JWE
    /// algorithm derives from it at the token pipeline.</param>
    /// <param name="key">Optional asymmetric key; exported as PKCS#8 PEM and not retained.</param>
    /// <returns>The created row.</returns>
    public static SchemataSecurity AddEncryptionRow(
        ISecurityStore<SchemataSecurity> store,
        string                           issuer,
        string                           algorithm = SecurityConstants.Algorithms.Rsa,
        AsymmetricAlgorithm?             key       = null
    ) {
        return AddRow(store, issuer, SecurityConstants.Usages.Encryption, algorithm, key);
    }

    /// <summary>Builds a ready-to-use <see cref="TokenService" /> over a store seeded
    /// with a signing row when <paramref name="seed" /> — plus an encryption row when
    /// <paramref name="encryption" /> — covering the constructor dependencies most unit tests
    /// do not exercise.</summary>
    public static TokenService CreateTokenService(
        SchemataAuthorizationOptions options,
        TestSecurityStore?           store      = null,
        string                       algorithm  = SecurityConstants.Algorithms.Rsa,
        bool                         encryption = false,
        TimeProvider?                time       = null,
        bool                         seed       = true
    ) {
        options.Issuer ??= "https://localhost";
        store ??= new();
        if (seed) {
            AddSigningRow(store, options.Issuer, algorithm);
        }

        if (encryption) {
            AddEncryptionRow(store, options.Issuer);
        }

        return new(
            store,
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
            Options.Create(new SchemataSecurityOptions()),
            Options.Create(options),
            time);
    }

    private static SchemataSecurity AddRow(
        ISecurityStore<SchemataSecurity> store,
        string                           issuer,
        string                           usage,
        string                           algorithm,
        AsymmetricAlgorithm?             key
    ) {
        using var minted = key is null ? CreateKey(algorithm) : null;
        var       row    = new SchemataSecurity {
            Parent     = SecurityParents.Issuer(issuer),
            Key        = $"eph-{Guid.NewGuid():n}",
            Kind       = SecurityConstants.Kinds.PrivateKey,
            Usage      = usage,
            Algorithm  = algorithm,
            Kid        = $"eph-{Guid.NewGuid():n}",
            Value      = (minted ?? key!).ExportPkcs8PrivateKeyPem(),
            Status     = SecurityConstants.Statuses.Valid,
            CreateTime = DateTime.UtcNow,
        };

        // Store stubs complete synchronously; the blocking wait never parks a thread in tests.
        return store.CreateAsync(row).GetAwaiter().GetResult()!;
    }

    private static AsymmetricAlgorithm CreateKey(string algorithm) {
        return algorithm switch {
            SecurityConstants.Algorithms.P256 => ECDsa.Create(ECCurve.NamedCurves.nistP256),
            SecurityConstants.Algorithms.P384 => ECDsa.Create(ECCurve.NamedCurves.nistP384),
            SecurityConstants.Algorithms.P521 => ECDsa.Create(ECCurve.NamedCurves.nistP521),
            _                                 => RSA.Create(2048),
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
