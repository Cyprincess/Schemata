using Microsoft.AspNetCore.Http;

namespace Schemata.Authorization.Foundation.Services;

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