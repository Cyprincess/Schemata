using System.Collections.Generic;
using System.Text;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     The signature metadata parameters of RFC 9421 section 2.3.
/// </summary>
/// <remarks>
///     Serialization order is fixed at <c>created</c>, <c>expires</c>, <c>keyid</c>, <c>alg</c>,
///     <c>nonce</c>, <c>tag</c> — the one immutable order section 2.3 requires for the life of a
///     signature. Absent members are skipped.
/// </remarks>
public sealed record SignatureParameters
{
    /// <summary>Creation time as a UNIX timestamp.</summary>
    public long? Created { get; init; }

    /// <summary>Expiration time as a UNIX timestamp.</summary>
    public long? Expires { get; init; }

    /// <summary>The verification key identifier.</summary>
    public string? KeyId { get; init; }

    /// <summary>The algorithm name from the HTTP Signature Algorithms registry (RFC 9421 section 6.2).</summary>
    public string? Algorithm { get; init; }

    /// <summary>A random unique value for this signature.</summary>
    public string? Nonce { get; init; }

    /// <summary>An application-specific tag.</summary>
    public string? Tag { get; init; }

    /// <summary>Serializes the parameters as the parameter block appended to the covered-components Inner List, for example <c>;created=1618884473;keyid="test-key"</c>.</summary>
    /// <returns>The serialized parameter block; empty when every member is absent.</returns>
    public string Serialize() {
        var builder = new StringBuilder();
        if (Created is { } created) {
            builder.Append(";created=").Append(created);
        }
        if (Expires is { } expires) {
            builder.Append(";expires=").Append(expires);
        }
        if (KeyId is { } keyid) {
            builder.Append(";keyid=\"").Append(MessageComponentIdentifier.Escape(keyid)).Append('"');
        }
        if (Algorithm is { } alg) {
            builder.Append(";alg=\"").Append(MessageComponentIdentifier.Escape(alg)).Append('"');
        }
        if (Nonce is { } nonce) {
            builder.Append(";nonce=\"").Append(MessageComponentIdentifier.Escape(nonce)).Append('"');
        }
        if (Tag is { } tag) {
            builder.Append(";tag=\"").Append(MessageComponentIdentifier.Escape(tag)).Append('"');
        }

        return builder.ToString();
    }

    /// <summary>Parses the parameter block of a Signature-Input member, for example <c>;created=1618884473;keyid="test-key"</c>.</summary>
    /// <param name="text">The parameter block, including the leading semicolons.</param>
    /// <returns>The parsed parameters; unknown parameter names are ignored so registries beyond section 2.3 remain forward-compatible.</returns>
    /// <exception cref="InvalidArgumentException">The block is malformed.</exception>
    public static SignatureParameters Parse(string text) {
        var parameters = new SignatureParameters();
        var rest       = text;
        while (!string.IsNullOrEmpty(rest)) {
            if (rest[0] != ';') {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
            }

            rest = rest[1..];
            var terminator = rest.IndexOf(';');
            var segment    = terminator < 0 ? rest : rest[..terminator];
            rest = terminator < 0 ? string.Empty : rest[terminator..];

            var equal = segment.IndexOf('=');
            if (equal < 1) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
            }

            var name  = segment[..equal];
            var value = segment[(equal + 1)..];
            parameters = name switch {
                SignatureConstants.Parameters.Created => parameters with { Created = ParseInteger(value, text) },
                SignatureConstants.Parameters.Expires => parameters with { Expires = ParseInteger(value, text) },
                SignatureConstants.Parameters.KeyId   => parameters with { KeyId = ParseString(value, text) },
                SignatureConstants.Parameters.Alg     => parameters with { Algorithm = ParseString(value, text) },
                SignatureConstants.Parameters.Nonce   => parameters with { Nonce = ParseString(value, text) },
                SignatureConstants.Parameters.Tag     => parameters with { Tag = ParseString(value, text) },
                _                                     => parameters,
            };
        }

        return parameters;
    }

    private static long ParseInteger(string value, string text) {
        if (!long.TryParse(value, out var parsed)) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
        }

        return parsed;
    }

    private static string ParseString(string value, string text) {
        if (value.Length < 2 || !value.StartsWith('"') || !value.EndsWith('"')) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
        }

        return MessageComponentIdentifier.Unescape(value[1..^1]);
    }

    /// <summary>Serializes the covered components and parameters as the <c>@signature-params</c> component value (RFC 9421 sections 2.3 and 4.1).</summary>
    /// <param name="components">The ordered covered component identifiers.</param>
    /// <param name="parameters">The signature parameters.</param>
    /// <returns>The Inner List value shared by the signature base and the Signature-Input field.</returns>
    public static string SerializeValue(
        IReadOnlyList<MessageComponentIdentifier> components,
        SignatureParameters                       parameters
    ) {
        var builder = new StringBuilder();
        builder.Append('(');
        for (var index = 0; index < components.Count; index++) {
            if (index > 0) {
                builder.Append(' ');
            }

            builder.Append(components[index]);
        }

        builder.Append(')');
        builder.Append(parameters.Serialize());
        return builder.ToString();
    }
}
