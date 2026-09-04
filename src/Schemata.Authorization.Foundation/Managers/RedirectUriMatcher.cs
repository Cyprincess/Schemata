using System;

namespace Schemata.Authorization.Foundation.Managers;

/// <summary>
///     Exact-match redirect URI comparison with the single RFC 8252 exemption: any port is
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

        if (string.Equals(registered, requested, StringComparison.Ordinal)) {
            return true;
        }

        if (!Uri.TryCreate(registered, UriKind.Absolute, out var reg)
         || !Uri.TryCreate(requested, UriKind.Absolute, out var req)) {
            return false;
        }

        if (reg.Scheme != Uri.UriSchemeHttp || req.Scheme != Uri.UriSchemeHttp) {
            return false;
        }

        var isLoopback = (Uri u) => u.Host is "127.0.0.1" or "[::1]" or "::1";
        if (!isLoopback(reg) || !isLoopback(req)) {
            return false;
        }

        return reg.Host == req.Host
            && string.Equals(reg.AbsolutePath, req.AbsolutePath, StringComparison.Ordinal)
            && string.Equals(reg.Query, req.Query, StringComparison.Ordinal);
    }
}