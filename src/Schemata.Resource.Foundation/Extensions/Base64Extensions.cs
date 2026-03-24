// ReSharper disable once CheckNamespace

namespace System;

/// <summary>
/// Extension methods for Base64 URL-safe encoding and decoding.
/// </summary>
public static class Base64Extensions
{
    /// <summary>
    /// Converts a byte array to a Base64 URL-safe string (no padding, <c>-</c> and <c>_</c> instead of <c>+</c> and <c>/</c>).
    /// </summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The Base64 URL-safe string.</returns>
    public static string ToBase64UrlString(this byte[] bytes) {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes a Base64 URL-safe string back to a byte array.
    /// </summary>
    /// <param name="string">The Base64 URL-safe string to decode.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] FromBase64UrlString(this string @string) {
        var base64 = @string.Replace('_', '/').Replace('-', '+');
        switch (base64.Length % 4) {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }
}
