using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Application requirements enforced during verification (RFC 9421 section 3.2.1). These are
///     configuration for the verifier, not per-call runtime flags.
/// </summary>
public sealed class HttpMessageSignatureVerificationOptions
{
    /// <summary>The signature label to verify; <see langword="null" /> requires the message to carry exactly one signature.</summary>
    public string? Label { get; set; }

    /// <summary>Covered components the signature must include, in textual identifier form (for example <c>"@method"</c>, <c>"content-digest"</c>). Verification fails when any is absent from the signature's covered set.</summary>
    public IList<string> RequiredComponents { get; } = new List<string>();

    /// <summary>Maximum accepted age of the <c>created</c> timestamp; signatures older than this fail. <see langword="null" /> imposes no age limit.</summary>
    public TimeSpan? MaximumSignatureAge { get; set; }

    /// <summary>Registry names of acceptable algorithms; empty accepts every registered algorithm. A signature resolving to another algorithm fails.</summary>
    public ISet<string> AllowedAlgorithms { get; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
///     Verifies message signatures per RFC 9421 section 3.2: parses the Signature-Input and
///     Signature fields (sections 4.1 and 4.2), enforces the application requirements of section
///     3.2.1, recreates the signature base (section 2.5), and applies the HTTP_VERIFY primitive
///     of the resolved section 3.3 algorithm.
/// </summary>
/// <remarks>
///     The <c>@signature-params</c> line is rebuilt from the raw Signature-Input member value
///     verbatim, as section 3.2 step 7 requires, so parameter order chosen by the signer is
///     preserved byte for byte.
/// </remarks>
public sealed class HttpMessageSignatureVerifier(TimeProvider? time = null, HttpMessageSignatureVerificationOptions? options = null)
{
    private readonly TimeProvider                             _time    = time ?? TimeProvider.System;
    private readonly HttpMessageSignatureVerificationOptions? _options = options;

    /// <summary>Verifies the applicable signature of a message against directly supplied key material.</summary>
    /// <param name="message">The target message view carrying the Signature-Input and Signature fields.</param>
    /// <param name="key">The verification key material.</param>
    /// <param name="options">Per-call requirements overriding the configured ones; <see langword="null" /> uses the configured set.</param>
    /// <returns><see langword="true" /> when the signature verifies and meets every requirement; <see langword="false" /> on any failure.</returns>
    public bool Verify(SignatureMessage message, SecurityKeyMaterial key, HttpMessageSignatureVerificationOptions? options = null) {
        return Verify(message, _ => key, options);
    }

    /// <summary>Verifies the applicable signature of a message, resolving key material by the signature's <c>keyid</c> parameter.</summary>
    /// <param name="message">The target message view carrying the Signature-Input and Signature fields.</param>
    /// <param name="resolveKey">Resolves the <c>keyid</c> (<see langword="null" /> when the signature carries none) to key material; returning <see langword="null" /> fails verification (section 3.2 step 5).</param>
    /// <param name="options">Per-call requirements overriding the configured ones; <see langword="null" /> uses the configured set.</param>
    /// <returns><see langword="true" /> when the signature verifies and meets every requirement; <see langword="false" /> on any failure.</returns>
    public bool Verify(
        SignatureMessage                          message,
        Func<string?, SecurityKeyMaterial?>       resolveKey,
        HttpMessageSignatureVerificationOptions?  options = null
    ) {
        options ??= _options ?? new();

        try {
            var inputs     = SignatureFieldParser.ParseInputs(message.GetFieldValues(SignatureConstants.Fields.SignatureInput));
            var signatures = SignatureFieldParser.ParseSignatures(message.GetFieldValues(SignatureConstants.Fields.Signature));
            if (inputs.Count == 0 || signatures.Count == 0) {
                return false;
            }

            var label = options.Label ?? (inputs.Count == 1 ? inputs.Keys.Single() : null);
            if (label is null
             || !inputs.TryGetValue(label, out var input)
             || !signatures.TryGetValue(label, out var presented)) {
                return false;
            }

            var parameters = input.Parameters;
            var now        = _time.GetUtcNow().ToUnixTimeSeconds();
            if (options.MaximumSignatureAge is { } maximum) {
                if (parameters.Created is not { } created || now - created > maximum.TotalSeconds) {
                    return false;
                }
            }
            if (parameters.Expires is { } expires && now > expires) {
                return false;
            }

            var covered = new HashSet<string>(input.Components.Select(component => component.ToString()), StringComparer.Ordinal);
            foreach (var required in options.RequiredComponents) {
                if (!covered.Contains(MessageComponentIdentifier.Parse(required).ToString())) {
                    return false;
                }
            }

            var key = resolveKey(parameters.KeyId);
            if (key is null) {
                return false;
            }

            var algorithm = HttpSignatureAlgorithms.Resolve(parameters.Algorithm, key);
            if (options.AllowedAlgorithms.Count > 0 && !options.AllowedAlgorithms.Contains(algorithm.Name)) {
                return false;
            }

            var signatureBase = SignatureBase.Create(message, input.Components, input.ParametersValue);
            return algorithm.Verify(SignatureBase.GetBytes(signatureBase), key, presented);
        } catch (Exception exception) when (exception is SchemataException or FormatException or CryptographicException) {
            return false;
        }
    }
}

/// <summary>
///     Parses the Dictionary Structured Fields of RFC 9421 sections 4.1 and 4.2 into their
///     labeled members.
/// </summary>
internal static class SignatureFieldParser
{
    public sealed record ParsedInput(
        string                                    ParametersValue,
        IReadOnlyList<MessageComponentIdentifier> Components,
        SignatureParameters                       Parameters
    );

    public static IReadOnlyDictionary<string, ParsedInput> ParseInputs(IReadOnlyList<string> fields) {
        var result = new Dictionary<string, ParsedInput>(StringComparer.Ordinal);
        foreach (var member in SplitMembers(fields)) {
            var (label, value) = SplitMember(member);
            if (!value.StartsWith('(')) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = member });
            }

            var close = CloseOfInnerList(value);
            var inner = value[1..close];
            // Section 3.2 step 7: the raw member value becomes the @signature-params value verbatim.
            var parametersValue = value;

            var components = new List<MessageComponentIdentifier>();
            foreach (var item in SplitItems(inner)) {
                components.Add(MessageComponentIdentifier.Parse(item));
            }

            var parameters = SignatureParameters.Parse(value[(close + 1)..]);
            if (!result.TryAdd(label, new(parametersValue, components, parameters))) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = member });
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<string, byte[]> ParseSignatures(IReadOnlyList<string> fields) {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var member in SplitMembers(fields)) {
            var (label, value) = SplitMember(member);
            if (value.Length < 2 || !value.StartsWith(':') || !value.EndsWith(':')) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = member });
            }

            if (!result.TryAdd(label, Convert.FromBase64String(value[1..^1]))) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = member });
            }
        }

        return result;
    }

    // Splits a Dictionary value into members on top-level commas: commas inside quoted strings
    // and inside the Inner List parentheses do not separate members.
    private static IEnumerable<string> SplitMembers(IReadOnlyList<string> fields) {
        foreach (var field in fields) {
            var start = 0;
            var depth = 0;
            var quoted = false;
            for (var index = 0; index < field.Length; index++) {
                var character = field[index];
                if (quoted) {
                    if (character == '\\') {
                        index++;
                    } else if (character == '"') {
                        quoted = false;
                    }
                } else if (character == '"') {
                    quoted = true;
                } else if (character == '(') {
                    depth++;
                } else if (character == ')') {
                    depth--;
                } else if (character == ',' && depth == 0) {
                    yield return field[start..index].Trim();
                    start = index + 1;
                }
            }

            var tail = field[start..].Trim();
            if (tail.Length > 0) {
                yield return tail;
            }
        }
    }

    private static (string Label, string Value) SplitMember(string member) {
        var depth  = 0;
        var quoted = false;
        for (var index = 0; index < member.Length; index++) {
            var character = member[index];
            if (quoted) {
                if (character == '\\') {
                    index++;
                } else if (character == '"') {
                    quoted = false;
                }
            } else if (character == '"') {
                quoted = true;
            } else if (character == '(') {
                depth++;
            } else if (character == ')') {
                depth--;
            } else if (character == '=' && depth == 0) {
                var label = member[..index].Trim();
                if (label.Length == 0) {
                    break;
                }

                return (label, member[(index + 1)..].Trim());
            }
        }

        throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = member });
    }

    private static int CloseOfInnerList(string value) {
        var depth  = 0;
        var quoted = false;
        for (var index = 0; index < value.Length; index++) {
            var character = value[index];
            if (quoted) {
                if (character == '\\') {
                    index++;
                } else if (character == '"') {
                    quoted = false;
                }
            } else if (character == '"') {
                quoted = true;
            } else if (character == '(') {
                depth++;
            } else if (character == ')') {
                depth--;
                if (depth == 0) {
                    return index;
                }
            }
        }

        throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = value });
    }

    // Splits Inner List content into items on spaces outside quoted strings; parameters stay
    // attached to their item.
    private static IEnumerable<string> SplitItems(string inner) {
        var start  = 0;
        var quoted = false;
        var found  = false;
        for (var index = 0; index < inner.Length; index++) {
            var character = inner[index];
            if (quoted) {
                if (character == '\\') {
                    index++;
                } else if (character == '"') {
                    quoted = false;
                }
            } else if (character == '"') {
                quoted = true;
                found  = true;
            } else if (!found && character != ' ') {
                // RFC 9651: every Inner List item here is a String; anything else is malformed.
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = inner });
            } else if (character == ' ') {
                if (found) {
                    yield return inner[start..index];
                    found = false;
                }

                start = index + 1;
            }
        }

        if (found) {
            yield return inner[start..];
        }
    }
}
