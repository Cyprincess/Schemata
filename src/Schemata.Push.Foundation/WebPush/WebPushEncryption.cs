using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Schemata.Push.Foundation.WebPush;

/// <summary>
///     RFC 8291 message encryption: ECDH P-256 key agreement combined with the subscription
///     authentication secret (sections 3.1-3.3), CEK/nonce derivation per RFC 8188, and a single
///     aes128gcm record (section 4 mandates one record; the sequence number stays zero).
/// </summary>
internal static class WebPushEncryption
{
    /// <summary>The <c>rs</c> parameter written into the aes128gcm content coding header.</summary>
    public const int RecordSize = 4096;

    /// <summary>
    ///     Largest encryptable plaintext: 4096-octet body limit (RFC 8030 section 7.2) minus the
    ///     86-octet header, the padding delimiter, and the 16-octet authentication tag
    ///     (RFC 8291 section 4).
    /// </summary>
    public const int MaxPayloadLength = 3993;

    private const int SaltLength     = 16;
    private const int AuthSecretLength = 16;
    private const int PublicKeyLength = 65;

    /// <summary>Encrypts <paramref name="plaintext" /> for one subscription with a fresh ephemeral key and salt.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] subscriberPublicKey, byte[] authSecret) {
        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, subscriberPublicKey, authSecret, sender, RandomNumberGenerator.GetBytes(SaltLength));
    }

    /// <summary>
    ///     Encrypts with caller-supplied ephemeral key and salt; the parameterless-randomness
    ///     overload is the production path, this overload exists for known-answer verification.
    /// </summary>
    internal static byte[] Encrypt(
        byte[]           plaintext,
        byte[]           subscriberPublicKey,
        byte[]           authSecret,
        ECDiffieHellman  sender,
        byte[]           salt
    ) {
        if (plaintext.Length > MaxPayloadLength) {
            throw new ArgumentOutOfRangeException(
                nameof(plaintext),
                $"a push message payload is limited to {MaxPayloadLength} octets (RFC 8291 section 4)");
        }

        if (salt.Length != SaltLength) {
            throw new ArgumentOutOfRangeException(nameof(salt), $"the salt is {SaltLength} octets (RFC 8188 section 2.1)");
        }

        if (authSecret.Length != AuthSecretLength) {
            throw new ArgumentException(
                $"the authentication secret is {AuthSecretLength} octets (RFC 8291 section 3.2)",
                nameof(authSecret));
        }

        using var receiver = ImportPublicKey(subscriberPublicKey);
        var shared       = sender.DeriveRawSecretAgreement(receiver.PublicKey);
        var senderPublic = ExportPublicKey(sender);

        // RFC 8291 section 3.3: HKDF-Extract(salt=auth_secret, IKM=ecdh_secret), then expand
        // with key_info = "WebPush: info" || 0x00 || ua_public || as_public.
        var keyInfo = new byte[14 + PublicKeyLength + PublicKeyLength];
        "WebPush: info\0"u8.CopyTo(keyInfo);
        subscriberPublicKey.CopyTo(keyInfo, 14);
        senderPublic.CopyTo(keyInfo, 14 + PublicKeyLength);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret, keyInfo);

        // RFC 8291 section 3.4 (the RFC 8188 derivation applied to the combined IKM).
        var prk   = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek   = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        // RFC 8188 section 2.1 header: salt || rs || idlen || keyid; one record carries
        // plaintext || 0x02 (padding delimiter, final record, RFC 8188 section 2.2).
        var body = new byte[SaltLength + 4 + 1 + PublicKeyLength + plaintext.Length + 1 + 16];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(SaltLength, 4), RecordSize);
        body[SaltLength + 4] = PublicKeyLength;
        senderPublic.CopyTo(body, SaltLength + 5);

        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record, 0);
        record[^1] = 0x02;

        using var aes = new AesGcm(cek, 16);
        var content = body.AsSpan(SaltLength + 5 + PublicKeyLength);
        aes.Encrypt(nonce, record, content[..record.Length], content[record.Length..]);
        return body;
    }

    private static ECDiffieHellman ImportPublicKey(byte[] publicKey) {
        if (publicKey.Length != PublicKeyLength || publicKey[0] != 0x04) {
            throw new ArgumentException(
                "the subscription public key is a 65-octet X9.62 uncompressed P-256 point (RFC 8291 section 4)",
                nameof(publicKey));
        }

        return ECDiffieHellman.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new() {
                X = publicKey[1..33],
                Y = publicKey[33..],
            },
        });
    }

    private static byte[] ExportPublicKey(ECDiffieHellman key) {
        var parameters = key.ExportParameters(false);
        var result = new byte[PublicKeyLength];
        result[0] = 0x04;
        parameters.Q.X!.CopyTo(result, 1);
        parameters.Q.Y!.CopyTo(result, 33);
        return result;
    }
}
