using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Schemata.Authorization.Foundation.Services;

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
        http.Response.Cookies.Append(cookieName, state, new() {
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