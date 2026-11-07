using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using Schemata.Abstractions.Linking;

namespace Schemata.Common.Linking;

/// <summary>
///     Serializes <see cref="WebLink" /> values into the RFC 8288 §3 link-value syntax shared by
///     every transport that projects links.
/// </summary>
public static class LinkValue
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    /// <summary>
    ///     Serializes links into one comma-separated link-value list.
    /// </summary>
    /// <param name="links">The links to serialize; duplicates are emitted once.</param>
    /// <returns>The serialized value, or an empty string when there are no links.</returns>
    public static string Serialize(IEnumerable<WebLink>? links) {
        if (links is null) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in links) {
            var value = Serialize(link);

            if (!seen.Add(value)) {
                continue;
            }

            if (builder.Length > 0) {
                builder.Append(", ");
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Serializes one link into a link-value.
    /// </summary>
    /// <param name="link">The link to serialize.</param>
    /// <returns>The serialized link-value.</returns>
    /// <exception cref="ArgumentException">The target or a parameter is not representable.</exception>
    public static string Serialize(WebLink link) {
        var builder = new StringBuilder();

        builder.Append('<');
        builder.Append(ValidateTarget(link.Target));
        builder.Append('>');

        AppendParameter(builder, "rel", link.Rel);
        AppendOptionalParameter(builder, "anchor", link.Anchor);
        AppendOptionalParameter(builder, "hreflang", link.Hreflang);
        AppendOptionalParameter(builder, "media", link.Media);
        AppendOptionalParameter(builder, "title", link.Title);
        if (link.TitleStar is { } titleStar) AppendExtValue(builder, titleStar);
        AppendOptionalParameter(builder, "type", link.Type);

        if (link.Extensions is not null) {
            foreach (var extension in link.Extensions) {
                if (IsReservedParameter(extension.Key)) {
                    throw new ArgumentException(
                        $"The link extension '{extension.Key}' collides with a serialisation-defined parameter.",
                        nameof(link));
                }

                AppendParameter(builder, extension.Key, extension.Value);
            }
        }

        return builder.ToString();
    }

    public static bool IsRepresentable(WebLink link) {
        if (NormalizeTarget(link.Target) is null || !IsParameterValue(link.Rel)
         || !IsParameterValue(link.Anchor) || !IsParameterValue(link.Hreflang)
         || !IsParameterValue(link.Media) || !IsParameterValue(link.Title)
         || !IsParameterValue(link.Type) || link.TitleStar is { } titleStar && !IsExtValue(titleStar)) return false;
        if (link.Extensions is null) return true;
        foreach (var extension in link.Extensions) {
            if (IsReservedParameter(extension.Key) || !IsToken(extension.Key) || !IsParameterValue(extension.Value)) return false;
        }
        return true;
    }

    /// <summary>
    ///     Reports whether <paramref name="name" /> is a serialization-defined link parameter.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns><see langword="true" /> when the parameter is reserved.</returns>
    public static bool IsReservedParameter(string name) =>
        name.ToLowerInvariant() is "rel" or "rev" or "anchor" or "hreflang" or "media" or "title" or "title*" or "type";

    public static bool IsExtValue(string value) {
        var first = value.IndexOf('\'');
        if (first < 0 || !value.AsSpan(0, first).Equals("UTF-8".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        var second = value.IndexOf('\'', first + 1);
        if (second < 0) return false;
        var language = value.AsSpan(first + 1, second - first - 1);
        var segment = 0;
        for (var index = 0; index < language.Length; index++) {
            var current = language[index];
            if (current == '-') {
                if (segment == 0 || segment > 8) return false;
                segment = 0;
            } else {
                if (!IsAsciiLetter(current) && !(current is >= '0' and <= '9' && index > 0)) return false;
                segment++;
            }
        }
        if (language.Length > 0 && (segment == 0 || segment > 8)) return false;
        var bytes = new byte[value.Length - second - 1];
        var count = 0;
        for (var index = second + 1; index < value.Length; index++) {
            var current = value[index];
            if (current == '%') {
                if (index + 2 >= value.Length || !IsHexDigit(value[index + 1]) || !IsHexDigit(value[index + 2])) return false;
                bytes[count++] = byte.Parse(value.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                index += 2;
            } else {
                if (!(current is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                    or '!' or '#' or '$' or '&' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~')) return false;
                bytes[count++] = (byte)current;
            }
        }
        try {
            var decoded = Utf8.GetString(bytes.AsSpan(0, count));
            foreach (var current in decoded) if (char.IsControl(current)) return false;
            return true;
        } catch (DecoderFallbackException) {
            return false;
        }
    }

    /// <summary>
    ///     Reports whether <paramref name="target" /> starts with a URI scheme.
    /// </summary>
    /// <param name="target">The link target.</param>
    /// <returns><see langword="true" /> when the target carries a scheme.</returns>
    public static bool HasScheme(string target) {
        if (target.Length < 2 || !IsAsciiLetter(target[0])) {
            return false;
        }

        var index = 1;

        while (index < target.Length && IsSchemeChar(target[index])) {
            index++;
        }

        return index < target.Length && target[index] == ':';
    }

    /// <summary>
    ///     Reports whether <paramref name="target" /> is a Windows drive path rather than a URI.
    /// </summary>
    /// <param name="target">The link target.</param>
    /// <returns><see langword="true" /> when the target is a drive path.</returns>
    public static bool IsDrivePath(string target) =>
        target.Length >= 3 && IsAsciiLetter(target[0]) && target[1] == ':' && target[2] is '/' or '\\';

    /// <summary>
    ///     Normalizes an absolute URI, mapping an internationalized host to its IDN form.
    /// </summary>
    /// <param name="absolute">The absolute URI.</param>
    /// <returns>The normalized absolute URI, or <see langword="null" /> when the host cannot be mapped.</returns>
    public static string? NormalizeAbsolute(Uri absolute) {
        foreach (var current in absolute.Host) {
            if (current > 0x7f) {
                try {
                    return new UriBuilder(absolute) { Host = absolute.IdnHost }.Uri.AbsoluteUri;
                } catch (UriFormatException) {
                    return null;
                }
            }
        }

        return absolute.AbsoluteUri;
    }

    /// <summary>
    ///     Validates that <paramref name="target" /> is a URI reference.
    /// </summary>
    /// <param name="target">The link target.</param>
    /// <param name="allowNonAscii">Whether non-ASCII characters are permitted before normalization.</param>
    /// <returns><see langword="true" /> when the target is a URI reference.</returns>
    public static bool IsUriReference(string target, bool allowNonAscii) {
        for (var index = 0; index < target.Length; index++) {
            var current = target[index];

            if (current == '%') {
                if (index + 2 >= target.Length || !IsHexDigit(target[index + 1]) || !IsHexDigit(target[index + 2])) {
                    return false;
                }

                index += 2;

                continue;
            }

            if (current > 0x7f && allowNonAscii) {
                continue;
            }

            if (!IsUriChar(current)) {
                return false;
            }
        }

        return true;
    }

    private static string ValidateTarget(string target) => NormalizeTarget(target)
        ?? throw new ArgumentException("The link target is not a representable URI-Reference.", nameof(target));

    private static string? NormalizeTarget(string target) {
        if (string.IsNullOrEmpty(target)) return null;
        if (!HasScheme(target)) return IsUriReference(target, false) ? target : null;
        if (!IsUriReference(target, true)) return null;
        if (IsDrivePath(target)) return target;
        return Uri.TryCreate(target, UriKind.Absolute, out var absolute) ? NormalizeAbsolute(absolute) : null;
    }

    private static bool IsParameterValue(string? value) {
        if (value is null) return true;
        foreach (var current in value) {
            if (current != '\t' && (current < 0x20 || current == 0x7f)) return false;
        }
        return true;
    }

    private static bool IsHexDigit(char current) => current is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool IsAsciiLetter(char current) => current is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsSchemeChar(char current) =>
        IsAsciiLetter(current) || current is >= '0' and <= '9' or '+' or '-' or '.';

    private static bool IsUriChar(char current) =>
        current is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '-' or '.' or '_' or '~'
            or ':' or '/' or '?' or '#' or '[' or ']' or '@'
            or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=';

    private static void AppendExtValue(StringBuilder builder, string value) {
        if (!IsExtValue(value)) throw new ArgumentException("The title* parameter must be a UTF-8 ext-value.", nameof(value));
        builder.Append("; title*=").Append(value);
    }

    private static void AppendOptionalParameter(StringBuilder builder, string name, string? value) {
        if (value is not null) {
            AppendParameter(builder, name, value);
        }
    }

    private static void AppendParameter(StringBuilder builder, string name, string value) {
        if (!IsToken(name)) {
            throw new ArgumentException($"The link parameter name '{name}' is not an HTTP token.", nameof(name));
        }
        if (!IsParameterValue(value)) throw new ArgumentException($"The link parameter '{name}' contains a control character.", nameof(value));

        builder.Append("; ").Append(name).Append("=\"");

        foreach (var current in value) {
            switch (current) {
                case '"':
                case '\\':
                    builder.Append('\\').Append(current);

                    break;
                case '\t':
                    builder.Append(current);

                    break;
                default:
                    builder.Append(current);

                    break;
            }
        }

        builder.Append('"');
    }

    private static bool IsToken(string value) {
        if (value.Length == 0) {
            return false;
        }

        foreach (var current in value) {
            if (!IsTokenChar(current)) {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenChar(char current) =>
        current is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
