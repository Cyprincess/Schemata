using System;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Exact ordinal redirect URI comparison with the single RFC 8252 exemption: any port is
///     acceptable for <c>http</c> loopback IP literal URIs, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8252.html#section-7.3">
///         RFC 8252: OAuth 2.0 for Native Apps §7.3: Loopback Interface Redirection
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     <c>localhost</c> host names are deliberately out of scope: RFC 8252 §8.3 marks
///     <c>localhost</c> redirect URIs NOT RECOMMENDED (host-name resolution and firewall
///     exposure), and §7.3 defines the exemption only for the loopback IP literals
///     <c>127.0.0.1</c> and <c>::1</c>. A <c>localhost</c> registration still matches through
///     the exact-compare path.
/// </remarks>
internal static class RedirectUriMatcher
{
    public static bool Matches(string? registered, string? requested) {
        if (string.IsNullOrWhiteSpace(registered) || string.IsNullOrWhiteSpace(requested)) {
            return false;
        }

        // Parsing gates eligibility only; comparison below runs on the original strings.
        if (!Uri.TryCreate(registered, UriKind.Absolute, out var reg)
         || !Uri.TryCreate(requested, UriKind.Absolute, out var req)) {
            return false;
        }

        // RFC 6749 §3.1.2 forbids fragments on the redirection endpoint; userinfo would let an
        // exact-looking registration smuggle credentials, so both fail even on identical text.
        if (!string.IsNullOrEmpty(reg.UserInfo) || !string.IsNullOrEmpty(reg.Fragment)
         || !string.IsNullOrEmpty(req.UserInfo) || !string.IsNullOrEmpty(req.Fragment)) {
            return false;
        }

        if (string.Equals(registered, requested, StringComparison.Ordinal)) {
            return true;
        }

        var regFamily = LoopbackFamily(registered, reg);
        if (regFamily == Loopback.None || regFamily != LoopbackFamily(requested, req)) {
            return false;
        }

        return string.Equals(RemoveExplicitPort(registered), RemoveExplicitPort(requested), StringComparison.Ordinal);
    }

    private enum Loopback
    {
        None,
        V4,
        V6,
    }

    internal static bool IsSupportedLoopback(string raw, Uri parsed) {
        return LoopbackFamily(raw, parsed) != Loopback.None;
    }

    private static Loopback LoopbackFamily(string raw, Uri parsed) {
        if (parsed.Scheme != Uri.UriSchemeHttp || !raw.StartsWith("http://", StringComparison.Ordinal)) {
            return Loopback.None;
        }

        const int authorityStart = 7;
        var authorityEnd = raw.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0) {
            authorityEnd = raw.Length;
        }

        var authority = raw.AsSpan(authorityStart, authorityEnd - authorityStart);
        if (authority.StartsWith("[::1]", StringComparison.Ordinal)
         && (authority.Length == 5 || authority[5] == ':')) {
            return Loopback.V6;
        }

        const string ipv4 = "127.0.0.1";
        return authority.StartsWith(ipv4, StringComparison.Ordinal)
            && (authority.Length == ipv4.Length || authority[ipv4.Length] == ':')
            ? Loopback.V4
            : Loopback.None;
    }

    // Removes the authority's explicit ":port" from the raw URI text, leaving every other byte
    // (scheme spelling, host literal spelling, path, query, percent encoding) untouched.
    private static string RemoveExplicitPort(string uri) {
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) {
            return uri;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd   = uri.Length;
        for (var i = authorityStart; i < uri.Length; i++) {
            if (uri[i] is '/' or '?' or '#') {
                authorityEnd = i;
                break;
            }
        }

        int portStart;
        if (uri[authorityStart] == '[') {
            var close = uri.IndexOf(']', authorityStart, authorityEnd - authorityStart);
            if (close < 0) {
                return uri;
            }

            portStart = close + 1;
            if (portStart >= authorityEnd || uri[portStart] != ':') {
                return uri;
            }
        } else {
            portStart = uri.LastIndexOf(':', authorityEnd - 1, authorityEnd - authorityStart);
            if (portStart < authorityStart) {
                return uri;
            }
        }

        if (portStart + 1 == authorityEnd) {
            return uri;
        }

        for (var i = portStart + 1; i < authorityEnd; i++) {
            if (uri[i] is < '0' or > '9') {
                return uri;
            }
        }

        return string.Concat(uri.AsSpan(0, portStart), uri.AsSpan(authorityEnd));
    }
}
