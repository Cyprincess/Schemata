using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Options for one signing operation.
/// </summary>
public sealed class HttpMessageSignatureOptions
{
    /// <summary>The signature label; must be unique within the message. Defaults to <c>sig</c>.</summary>
    public string Label { get; set; } = "sig";

    /// <summary>The algorithm registry name emitted as the <c>alg</c> parameter; <see langword="null" /> infers the algorithm from the key material and omits the parameter.</summary>
    public string? Algorithm { get; set; }

    /// <summary>The key identifier emitted as the <c>keyid</c> parameter.</summary>
    public string? KeyId { get; set; }

    /// <summary>Lifetime after creation at which the signature expires; sets the <c>expires</c> parameter. <see langword="null" /> omits it.</summary>
    public TimeSpan? ExpiresAfter { get; set; }

    /// <summary>A random unique value emitted as the <c>nonce</c> parameter; <see langword="null" /> omits it.</summary>
    public string? Nonce { get; set; }

    /// <summary>The application-specific <c>tag</c> parameter.</summary>
    public string? Tag { get; set; }
}

/// <summary>
///     Signs messages per RFC 9421 section 3.1: builds the signature base (section 2.5) from the
///     covered components, signs it with the algorithm from the section 3.3 registry, and emits
///     the Signature-Input and Signature field values (sections 4.1 and 4.2).
/// </summary>
public sealed class HttpMessageSigner(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Signs a message view.</summary>
    /// <param name="message">The target message view.</param>
    /// <param name="components">The ordered covered component identifiers declared for the surface.</param>
    /// <param name="key">The signing key material from the security store.</param>
    /// <param name="options">Per-operation options; defaults apply when <see langword="null" />.</param>
    /// <returns>The labeled Signature-Input and Signature output.</returns>
    /// <exception cref="InvalidArgumentException">
    ///     A covered component cannot be resolved from the message, or the key material is not
    ///     appropriate for the chosen algorithm.
    /// </exception>
    public HttpMessageSignature Sign(
        SignatureMessage                          message,
        IReadOnlyList<MessageComponentIdentifier> components,
        SecurityKeyMaterial                       key,
        HttpMessageSignatureOptions?              options = null
    ) {
        options ??= new();
        RequireKey(options.Label);

        var algorithm = HttpSignatureAlgorithms.Resolve(options.Algorithm, key);

        var created = _time.GetUtcNow().ToUnixTimeSeconds();
        var parameters = new SignatureParameters {
            Created   = created,
            Expires   = options.ExpiresAfter is { } lifetime ? created + (long)lifetime.TotalSeconds : null,
            KeyId     = options.KeyId,
            Algorithm = options.Algorithm,
            Nonce     = options.Nonce,
            Tag       = options.Tag,
        };

        var parametersValue = SignatureParameters.SerializeValue(components, parameters);
        var signatureBase   = SignatureBase.Create(message, components, parametersValue);
        var signature       = algorithm.Sign(SignatureBase.GetBytes(signatureBase), key);

        return new(options.Label, parametersValue, $":{Convert.ToBase64String(signature)}:");
    }

    // RFC 9651 section 3.3.4: an sf-key starts with a lowercase letter, followed by lowercase
    // letters, digits, "_", "-", ".", or "*".
    private static void RequireKey(string label) {
        var valid = label.Length > 0 && label[0] is >= 'a' and <= 'z';
        for (var index = 1; valid && index < label.Length; index++) {
            valid = label[index] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '*';
        }

        if (!valid) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_LABEL_INVALID, new Dictionary<string, string?> { ["label"] = label });
        }
    }
}
