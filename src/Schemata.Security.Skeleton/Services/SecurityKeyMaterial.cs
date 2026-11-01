using System.Security.Cryptography;

namespace Schemata.Security.Skeleton.Services;

/// <summary>Domain-neutral key material loaded from a stored security row.</summary>
public abstract record SecurityKeyMaterial
{
    /// <summary>Symmetric key bytes from a secret row.</summary>
    /// <param name="Key">Raw key bytes.</param>
    public sealed record Symmetric(byte[] Key) : SecurityKeyMaterial;

    /// <summary>RSA key loaded from a private-key or public-key row.</summary>
    /// <param name="Key">The key instance; freshly imported per load — the caller owns and disposes it.</param>
    public sealed record RsaKey(RSA Key) : SecurityKeyMaterial;

    /// <summary>EC key loaded from a private-key or public-key row.</summary>
    /// <param name="Key">The key instance; freshly imported per load — the caller owns and disposes it.</param>
    public sealed record EcKey(ECDsa Key) : SecurityKeyMaterial;

    /// <summary>Public key in PEM form.</summary>
    /// <param name="Pem">PEM-encoded public key.</param>
    public sealed record PublicKeyPem(string Pem) : SecurityKeyMaterial;

    /// <summary>A single JWK as JSON; JOSE-domain parsing is deferred to the adapter.</summary>
    /// <param name="Json">JWK JSON.</param>
    public sealed record JwkJson(string Json) : SecurityKeyMaterial;

    /// <summary>A JWKS as JSON.</summary>
    /// <param name="Json">JWKS JSON.</param>
    public sealed record JwksJson(string Json) : SecurityKeyMaterial;

    /// <summary>URI locating a JWKS or public key; fetch-and-cache semantics apply.</summary>
    /// <param name="Uri">The material URI.</param>
    public sealed record KeyUri(string Uri) : SecurityKeyMaterial;
}