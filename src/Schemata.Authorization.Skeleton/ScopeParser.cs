using System;
using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Parses and compares scope strings.
///     Scope tokens are space-delimited and case-sensitive (ordinal),
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §3.3: Access Token Scope
///     </seealso>
///     .
/// </summary>
public static class ScopeParser
{
    private static readonly char[] Separator = [' '];

    /// <summary>Splits a space-delimited scope string into a case-sensitive set of tokens.</summary>
    public static HashSet<string> Parse(string? scope) {
        return string.IsNullOrWhiteSpace(scope)
            ? new(StringComparer.Ordinal)
            : new HashSet<string>(scope!.Split(Separator, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    /// <summary>
    ///     Checks whether every token in <paramref name="requested" /> is present in <paramref name="approved" />.
    ///     An empty <paramref name="requested" /> is always a subset.
    /// </summary>
    public static bool IsSubset(string? requested, string? approved) {
        var r = Parse(requested);
        return r.Count == 0 || r.IsSubsetOf(Parse(approved));
    }

    /// <summary>Checks whether two scope strings contain exactly the same tokens.</summary>
    public static bool SetEquals(string? a, string? b) { return Parse(a).SetEquals(Parse(b)); }

    /// <summary>Checks whether a scope string contains a specific token.</summary>
    public static bool Contains(string? scope, string token) {
        return !string.IsNullOrWhiteSpace(scope) && Parse(scope).Contains(token);
    }
}
