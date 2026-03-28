using System;
using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Parses and compares OAuth 2.0 scope strings per RFC 6749 §3.3.
///     Scope tokens are space-delimited and case-sensitive (ordinal).
/// </summary>
public static class ScopeParser
{
    private static readonly char[] Separator = [' '];

    public static HashSet<string> Parse(string? scope) {
        return string.IsNullOrWhiteSpace(scope)
            ? new(StringComparer.Ordinal)
            : new HashSet<string>(scope!.Split(Separator, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    public static bool IsSubset(string? requested, string? approved) {
        var r = Parse(requested);
        return r.Count == 0 || r.IsSubsetOf(Parse(approved));
    }

    public static bool SetEquals(string? a, string? b) { return Parse(a).SetEquals(Parse(b)); }

    public static bool Contains(string? scope, string token) {
        return !string.IsNullOrWhiteSpace(scope) && Parse(scope).Contains(token);
    }
}
