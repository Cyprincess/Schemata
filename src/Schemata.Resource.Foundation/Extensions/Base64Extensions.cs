// ReSharper disable once CheckNamespace

namespace System;

public static class Base64Extensions
{
    public static string ToBase64UrlString(this byte[] bytes) {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

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
