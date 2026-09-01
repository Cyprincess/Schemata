namespace Schemata.Tenancy.Foundation.Handlers;

internal static class TenantHostNormalizer
{
    internal static string? Normalize(string? host) {
        return string.IsNullOrWhiteSpace(host) ? null : host.Trim().ToLowerInvariant();
    }
}
