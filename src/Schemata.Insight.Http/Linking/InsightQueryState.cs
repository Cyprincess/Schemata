using Microsoft.AspNetCore.Http;

namespace Schemata.Insight.Http.Linking;

/// <summary>
///     Carries the query's page token from the action to the link contributor. The token travels in
///     the request body, which model binding consumes, so the response-shaping stage reads it here.
/// </summary>
internal static class InsightQueryState
{
    private static readonly object Key = new();

    internal static void SetPageToken(HttpContext context, string? pageToken) {
        if (!string.IsNullOrEmpty(pageToken)) {
            context.Items[Key] = pageToken;
        }
    }

    internal static string? GetPageToken(HttpContext context) =>
        context.Items.TryGetValue(Key, out var value) ? value as string : null;
}
