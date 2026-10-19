using System;
using System.Collections.Generic;
using System.Text;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     The single RFC 8288 <c>Link</c> header field projection of <see cref="WebLink" /> values.
/// </summary>
public static class LinkHeader
{
    /// <summary>
    ///     The HTTP field name.
    /// </summary>
    public const string Name = "Link";

    /// <summary>
    ///     Parses a <c>Link</c> header field value; see <see cref="LinkHeaderParser.Parse" />.
    /// </summary>
    /// <param name="value">The field value.</param>
    /// <param name="context">The URL of the representation carrying the header.</param>
    /// <returns>The parsed links.</returns>
    public static IReadOnlyList<WebLink> Parse(string? value, Uri? context = null) => LinkHeaderParser.Parse(value, context);

    /// <summary>
    ///     Serializes links into one <c>Link</c> header field value.
    /// </summary>
    /// <param name="links">The links to serialize; duplicates are emitted once.</param>
    /// <returns>The field value, or an empty string when there are no links.</returns>
    public static string Serialize(IEnumerable<WebLink>? links) {
        if (links is null) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in links) {
            var value = SerializeLink(link);

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

    internal static bool IsReservedParameter(string name) =>
        name.ToLowerInvariant() is "rel" or "anchor" or "hreflang" or "media" or "title" or "title*" or "type";

    internal static bool HasScheme(string target) {
        if (target.Length < 2 || !IsAsciiLetter(target[0])) {
            return false;
        }

        var index = 1;

        while (index < target.Length && IsSchemeChar(target[index])) {
            index++;
        }

        return index < target.Length && target[index] == ':';
    }

    internal static bool IsDrivePath(string target) =>
        target.Length >= 3 && IsAsciiLetter(target[0]) && target[1] == ':' && target[2] is '/' or '\\';

    internal static string? NormalizeAbsolute(Uri absolute) {
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

    private static string SerializeLink(WebLink link) {
        var builder = new StringBuilder();

        builder.Append('<');
        builder.Append(ValidateTarget(link.Target));
        builder.Append('>');

        AppendParameter(builder, "rel", link.Rel);
        AppendOptionalParameter(builder, "anchor", link.Anchor);
        AppendOptionalParameter(builder, "hreflang", link.Hreflang);
        AppendOptionalParameter(builder, "media", link.Media);
        AppendOptionalParameter(builder, "title", link.Title);
        AppendOptionalParameter(builder, "title*", link.TitleStar);
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

    private static string ValidateTarget(string target) {
        if (string.IsNullOrEmpty(target)) {
            throw new ArgumentException("A link target is required.", nameof(target));
        }

        if (HasScheme(target)) {
            if (!IsUriReference(target, true)) {
                throw new ArgumentException("The link target is not a URI-Reference.", nameof(target));
            }

            if (IsDrivePath(target)) {
                return target;
            }

            if (!Uri.TryCreate(target, UriKind.Absolute, out var absolute)) {
                throw new ArgumentException("The link target is not an absolute URI.", nameof(target));
            }

            return NormalizeAbsolute(absolute)
                ?? throw new ArgumentException($"The host of the link target '{target}' cannot be mapped to an IDN.",
                    nameof(target));
        }

        if (!IsUriReference(target, false)) {
            throw new ArgumentException("The link target is not a URI-Reference.", nameof(target));
        }

        return target;
    }

    internal static bool IsUriReference(string target, bool allowNonAscii) {
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

    private static bool IsHexDigit(char current) => current is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool IsAsciiLetter(char current) => current is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsSchemeChar(char current) =>
        IsAsciiLetter(current) || current is >= '0' and <= '9' or '+' or '-' or '.';

    private static bool IsUriChar(char current) =>
        current is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '-' or '.' or '_' or '~'
            or ':' or '/' or '?' or '#' or '[' or ']' or '@'
            or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=';

    private static void AppendOptionalParameter(StringBuilder builder, string name, string? value) {
        if (value is not null) {
            AppendParameter(builder, name, value);
        }
    }

    private static void AppendParameter(StringBuilder builder, string name, string value) {
        if (!IsToken(name)) {
            throw new ArgumentException($"The link parameter name '{name}' is not an HTTP token.", nameof(name));
        }

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
                    if (current < 0x20 || current == 0x7f) {
                        throw new ArgumentException(
                            $"The link parameter '{name}' contains the control character U+{(int)current:X4}.",
                            nameof(value));
                    }

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
