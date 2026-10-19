using System;
using System.Security.Cryptography;
using System.Text;

namespace Schemata.Authorization.Foundation.Services;

internal sealed class SessionStateFormulator
{
    public string Build(string clientId, string origin, string opUaState, string salt) {
        var bytes = Encoding.UTF8.GetBytes($"{clientId} {origin} {opUaState} {salt}");
        var hash  = SHA256.HashData(bytes);
        var b64   = Convert.ToBase64String(hash, 0, hash.Length, Base64FormattingOptions.None)
                          .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{b64}.{salt}";
    }

    public static string? OriginOf(string? redirectUri) {
        if (string.IsNullOrWhiteSpace(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) {
            return null;
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }
}