using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     A covered component identifier: a component name plus its ordered parameters, serialized
///     per the <c>component-identifier</c> rule of RFC 9421 section 2.5 (component name as an
///     sf-string followed by semicolon-separated parameters).
/// </summary>
/// <remarks>
///     Parameter order is significant: once chosen it cannot change without altering the
///     signature base. Instances preserve the order given at construction or parsing.
/// </remarks>
public sealed class MessageComponentIdentifier
{
    private MessageComponentIdentifier(string name, IReadOnlyList<KeyValuePair<string, string?>> parameters) {
        Name       = name;
        Parameters = parameters;
    }

    /// <summary>The component name: a lowercased HTTP field name (RFC 9421 section 2.1) or a derived component name starting with <c>@</c> (RFC 9421 section 2.2).</summary>
    public string Name { get; }

    /// <summary>The ordered parameters; a <see langword="null" /> value marks a Boolean flag parameter.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Parameters { get; }

    /// <summary>Whether the component is a derived component (name starts with <c>@</c>).</summary>
    public bool IsDerived => Name.StartsWith('@');

    /// <summary>Creates an identifier for an HTTP field. The name is lowercased as section 2.1 requires.</summary>
    /// <param name="name">The field name; must not start with <c>@</c>.</param>
    /// <param name="binaryWrapped">Set the <c>bs</c> Boolean flag (RFC 9421 section 2.1.3).</param>
    /// <param name="trailer">Set the <c>tr</c> Boolean flag (RFC 9421 section 2.1.4).</param>
    /// <param name="fromRequest">Set the <c>req</c> Boolean flag (RFC 9421 section 2.4).</param>
    /// <returns>The field identifier.</returns>
    /// <exception cref="InvalidArgumentException">The name is blank or starts with <c>@</c>.</exception>
    public static MessageComponentIdentifier Field(
        string name,
        bool   binaryWrapped = false,
        bool   trailer       = false,
        bool   fromRequest   = false
    ) {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = name });
        }

        var parameters = new List<KeyValuePair<string, string?>>(3);
        if (binaryWrapped) {
            parameters.Add(new(SignatureConstants.ComponentParameters.Bs, null));
        }
        if (trailer) {
            parameters.Add(new(SignatureConstants.ComponentParameters.Tr, null));
        }
        if (fromRequest) {
            parameters.Add(new(SignatureConstants.ComponentParameters.Req, null));
        }

        return new(name.ToLowerInvariant(), parameters);
    }

    /// <summary>Creates an identifier for a derived component registered by RFC 9421 section 2.2.</summary>
    /// <param name="name">The derived component name, including the leading <c>@</c>.</param>
    /// <param name="parameterName">Value of the <c>name</c> parameter; required for <c>@query-param</c>.</param>
    /// <param name="fromRequest">Set the <c>req</c> Boolean flag (RFC 9421 section 2.4).</param>
    /// <returns>The derived component identifier.</returns>
    /// <exception cref="InvalidArgumentException">The name is not a derived component name.</exception>
    public static MessageComponentIdentifier Derived(string name, string? parameterName = null, bool fromRequest = false) {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith('@')) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = name });
        }

        var parameters = new List<KeyValuePair<string, string?>>(2);
        if (parameterName is not null) {
            parameters.Add(new(SignatureConstants.ComponentParameters.Name, parameterName));
        }
        if (fromRequest) {
            parameters.Add(new(SignatureConstants.ComponentParameters.Req, null));
        }

        return new(name, parameters);
    }

    /// <summary>The value of the <c>name</c> parameter, or <see langword="null" /> when absent.</summary>
    public string? ParameterName => GetParameter(SignatureConstants.ComponentParameters.Name);

    /// <summary>Whether the <c>bs</c> Boolean flag is present.</summary>
    public bool IsBinaryWrapped => HasFlag(SignatureConstants.ComponentParameters.Bs);

    /// <summary>Whether the <c>tr</c> Boolean flag is present.</summary>
    public bool IsTrailer => HasFlag(SignatureConstants.ComponentParameters.Tr);

    /// <summary>Whether the <c>req</c> Boolean flag is present.</summary>
    public bool IsFromRequest => HasFlag(SignatureConstants.ComponentParameters.Req);

    /// <summary>Whether a Boolean flag parameter is present.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns><see langword="true" /> when the parameter is present with no value.</returns>
    public bool HasFlag(string name) {
        return Parameters.Any(parameter => parameter.Key == name && parameter.Value is null);
    }

    /// <summary>The value of a String parameter, or <see langword="null" /> when absent.</summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The parameter value.</returns>
    public string? GetParameter(string name) {
        foreach (var parameter in Parameters) {
            if (parameter.Key == name) {
                return parameter.Value;
            }
        }

        return null;
    }

    /// <summary>Parses an identifier from its textual form, for example <c>"@method"</c>,
    /// <c>content-digest</c>, or <c>"@query-param";name="Pet"</c>. The surrounding quotes of the
    /// component name are optional on input; the canonical serialization always emits them.</summary>
    /// <param name="text">The textual identifier.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="InvalidArgumentException">The text is not a well-formed component identifier.</exception>
    public static MessageComponentIdentifier Parse(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
        }

        var span = text.Trim();
        var name = ReadName(span, out var rest);

        var parameters = new List<KeyValuePair<string, string?>>();
        while (!string.IsNullOrEmpty(rest)) {
            if (rest[0] != ';') {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
            }

            rest = rest[1..];
            var parameter = ReadParameter(rest, out rest, text);
            parameters.Add(parameter);
        }

        if (!name.StartsWith('@')) {
            name = name.ToLowerInvariant();
        }

        return new(name, parameters);
    }

    /// <summary>Serializes the identifier per the <c>component-identifier</c> rule: the quoted component name followed by its parameters in order.</summary>
    /// <returns>The canonical textual form used in the signature base and the Signature-Input field.</returns>
    public override string ToString() {
        var builder = new StringBuilder();
        builder.Append('"').Append(Escape(Name)).Append('"');
        foreach (var (key, value) in Parameters) {
            builder.Append(';').Append(key);
            if (value is not null) {
                builder.Append("=\"").Append(Escape(value)).Append('"');
            }
        }

        return builder.ToString();
    }

    private static string ReadName(string span, out string rest) {
        if (span.StartsWith('"')) {
            var end = span.IndexOf('"', 1);
            if (end < 1) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = span });
            }

            rest = span[(end + 1)..];
            return span[1..end];
        }

        var separator = span.IndexOf(';');
        if (separator < 0) {
            rest = string.Empty;
            return span;
        }

        rest = span[separator..];
        return span[..separator];
    }

    private static KeyValuePair<string, string?> ReadParameter(string span, out string rest, string text) {
        var terminator = span.IndexOf(';');
        var segment    = terminator < 0 ? span : span[..terminator];
        rest = terminator < 0 ? string.Empty : span[terminator..];

        var equal = segment.IndexOf('=');
        if (equal < 0) {
            if (segment.Length == 0) {
                throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
            }

            return new(segment, null);
        }

        var key       = segment[..equal];
        var raw       = segment[(equal + 1)..];
        if (raw.Length < 2 || !raw.StartsWith('"') || !raw.EndsWith('"')) {
            throw new InvalidArgumentException(SchemataResources.SIGNATURE_COMPONENT_INVALID, new Dictionary<string, string?> { ["component"] = text });
        }

        return new(key, Unescape(raw[1..^1]));
    }

    internal static string Escape(string value) {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    internal static string Unescape(string value) {
        return value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
