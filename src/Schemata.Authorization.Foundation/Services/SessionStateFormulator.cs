using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
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
}

internal static class SessionStateContext
{
    private static readonly object Key = new();

    public static void Set(HttpContext http, string value) {
        http.Items[Key] = value;
    }

    public static bool TryGet(HttpContext http, out string? value) {
        value = http.Items.TryGetValue(Key, out var stored) ? stored as string : null;
        return value is not null;
    }
}

internal static class OpState
{
    private static readonly object Key = new();

    public static string GetOrCreate(HttpContext http, string cookieName) {
        if (http.Items.TryGetValue(Key, out var existing) && existing is string value) {
            return value;
        }

        var state = http.Request.Cookies[cookieName];
        if (string.IsNullOrWhiteSpace(state)) {
            state = Mint();
            Write(http, cookieName, state);
        }

        http.Items[Key] = state;
        return state;
    }

    public static string Rotate(HttpContext http, string cookieName) {
        var state = Mint();
        http.Items[Key] = state;
        Write(http, cookieName, state);
        return state;
    }

    private static void Write(HttpContext http, string cookieName, string state) {
        http.Response.Cookies.Append(cookieName, state, new CookieOptions {
            Secure   = true,
            SameSite = SameSiteMode.None,
            HttpOnly = false,
            Path     = "/",
        });
    }

    private static string Mint() {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }
}