using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     Parses RFC 8288 <c>Link</c> header field values into <see cref="WebLink" /> instances using
///     the Appendix B parsing algorithms.
/// </summary>
public static class LinkHeaderParser
{
    private static readonly char[] RelationSeparators = [' ', '\t'];

    /// <summary>
    ///     Parses a <c>Link</c> header field value.
    /// </summary>
    /// <param name="value">The field value. Multiple field lines may be joined with commas.</param>
    /// <param name="context">
    ///     The URL of the representation carrying the header. Relative targets and relative
    ///     <c>anchor</c> values are resolved against it per RFC 3986 §5; when it is
    ///     <see langword="null" /> the serialized values are kept as written.
    /// </param>
    /// <returns>
    ///     The parsed links. Malformed input yields the links parsed before the first syntax error; a
    ///     link-value with a missing relation type or an invalid target is dropped.
    /// </returns>
    public static IReadOnlyList<WebLink> Parse(string? value, Uri? context = null) {
        var links = new List<WebLink>();

        if (string.IsNullOrEmpty(value)) {
            return links;
        }

        var index = 0;

        while (index < value.Length) {
            SkipOws(value, ref index);

            while (index < value.Length && value[index] == ',') {
                index++;
                SkipOws(value, ref index);
            }

            if (index >= value.Length || value[index] != '<') {
                break;
            }

            var parsed = ParseLinkValue(value, ref index, context);

            if (parsed is null) {
                break;
            }

            links.AddRange(parsed);
        }

        return links;
    }

    private static List<WebLink>? ParseLinkValue(string value, ref int index, Uri? context) {
        index++;

        var end = value.IndexOf('>', index);

        if (end < 0) {
            return null;
        }

        var target = value[index..end];
        index = end + 1;

        var parameters = ParseParameters(value, ref index);
        var relations = SplitRelations(FirstParameter(parameters, "rel"));

        if (relations.Count == 0 || !TryResolve(context, target, out var resolvedTarget)) {
            return new List<WebLink>();
        }

        var anchor = FirstParameter(parameters, "anchor");
        string? resolvedAnchor = null;

        if (anchor is not null && !TryResolve(context, anchor, out resolvedAnchor)) {
            return new List<WebLink>();
        }

        var hreflang = FirstParameter(parameters, "hreflang");
        var media = FirstParameter(parameters, "media");
        var title = FirstParameter(parameters, "title");
        var titleStar = FirstParameter(parameters, "title*");
        var type = FirstParameter(parameters, "type");
        var extensions = BuildExtensions(parameters);
        var links = new List<WebLink>(relations.Count);

        foreach (var relation in relations) {
            links.Add(new WebLink(resolvedTarget, relation, resolvedAnchor, hreflang, media, title, titleStar, type,
                extensions));
        }

        return links;
    }

    private static List<KeyValuePair<string, string>> ParseParameters(string value, ref int index) {
        var parameters = new List<KeyValuePair<string, string>>();

        while (index < value.Length) {
            SkipOws(value, ref index);

            if (index >= value.Length || value[index] != ';') {
                break;
            }

            index++;
            SkipOws(value, ref index);

            var start = index;

            while (index < value.Length && value[index] is not (' ' or '\t' or '=' or ';' or ',')) {
                index++;
            }

            var name = value[start..index];

            SkipOws(value, ref index);

            var parameterValue = string.Empty;

            if (index < value.Length && value[index] == '=') {
                index++;
                SkipOws(value, ref index);

                parameterValue = index < value.Length && value[index] == '"'
                    ? ParseQuotedString(value, ref index)
                    : ParseTokenValue(value, ref index);
            }

            if (name.Length > 0) {
                parameters.Add(new(name.ToLowerInvariant(), parameterValue));
            }

            SkipOws(value, ref index);
        }

        return parameters;
    }

    private static string ParseQuotedString(string value, ref int index) {
        index++;

        var chars = new StringBuilder();

        while (index < value.Length) {
            var current = value[index];

            switch (current) {
                case '\\':
                    index++;

                    if (index >= value.Length) {
                        return chars.ToString();
                    }

                    chars.Append(value[index]);
                    index++;

                    break;
                case '"':
                    index++;

                    return chars.ToString();
                default:
                    chars.Append(current);
                    index++;

                    break;
            }
        }

        return chars.ToString();
    }

    private static string ParseTokenValue(string value, ref int index) {
        var start = index;

        while (index < value.Length && value[index] is not (';' or ',')) {
            index++;
        }

        var length = index - start;

        while (length > 0 && value[start + length - 1] is ' ' or '\t') {
            length--;
        }

        return value.Substring(start, length);
    }

    private static List<string> SplitRelations(string? value) {
        var relations = new List<string>();

        if (string.IsNullOrEmpty(value)) {
            return relations;
        }

        foreach (var relation in value.Split(RelationSeparators, StringSplitOptions.RemoveEmptyEntries)) {
            relations.Add(relation.ToLowerInvariant());
        }

        return relations;
    }

    private static IReadOnlyDictionary<string, string>? BuildExtensions(List<KeyValuePair<string, string>> parameters) {
        Dictionary<string, string>? extensions = null;

        foreach (var parameter in parameters) {
            if (LinkHeader.IsReservedParameter(parameter.Key)) {
                continue;
            }

            extensions ??= new(StringComparer.Ordinal);
            extensions.TryAdd(parameter.Key, parameter.Value);
        }

        return extensions is null ? null : new ReadOnlyDictionary<string, string>(extensions);
    }

    private static string? FirstParameter(List<KeyValuePair<string, string>> parameters, string name) {
        foreach (var parameter in parameters) {
            if (parameter.Key.Equals(name, StringComparison.Ordinal)) {
                return parameter.Value;
            }
        }

        return null;
    }

    private static bool TryResolve(Uri? context, string value, out string resolved) {
        resolved = value;

        if (string.IsNullOrEmpty(value)) {
            return false;
        }

        if (LinkHeader.HasScheme(value)) {
            if (LinkHeader.IsDrivePath(value)) {
                return LinkHeader.IsUriReference(value, true);
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var absolute)) {
                return false;
            }

            resolved = LinkHeader.NormalizeAbsolute(absolute) ?? absolute.AbsoluteUri;

            return true;
        }

        if (context is null) {
            return Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _);
        }

        if (!Uri.TryCreate(context, value, out var uri)) {
            return false;
        }

        resolved = LinkHeader.NormalizeAbsolute(uri) ?? uri.AbsoluteUri;

        return true;
    }

    private static void SkipOws(string value, ref int index) {
        while (index < value.Length && value[index] is ' ' or '\t') {
            index++;
        }
    }
}
