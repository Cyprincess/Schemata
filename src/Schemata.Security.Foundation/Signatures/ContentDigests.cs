using System;
using System.Security.Cryptography;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Computes Content-Digest field values (RFC 9530) so the body can participate in a
///     signature through the <c>content-digest</c> covered component (RFC 9421 section 7.2.8).
/// </summary>
public static class ContentDigests
{
    /// <summary>The <c>sha-256</c> content digest of a body.</summary>
    /// <param name="content">The message content bytes.</param>
    /// <returns>The field value, for example <c>sha-256=:X48E9qOokqqrvdts8nOJRJN3OWDUoyWxBf7kbu9DBPE=:</c>.</returns>
    public static string Sha256(ReadOnlySpan<byte> content) {
        return Format(SignatureConstants.DigestAlgorithms.Sha256, SHA256.HashData(content));
    }

    /// <summary>The <c>sha-512</c> content digest of a body.</summary>
    /// <param name="content">The message content bytes.</param>
    /// <returns>The field value, for example <c>sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:</c>.</returns>
    public static string Sha512(ReadOnlySpan<byte> content) {
        return Format(SignatureConstants.DigestAlgorithms.Sha512, SHA512.HashData(content));
    }

    private static string Format(string algorithm, byte[] hash) {
        return $"{algorithm}=:{Convert.ToBase64String(hash)}:";
    }
}
