using System.Security.Claims;

// ReSharper disable once CheckNamespace
namespace System;

public static class ClaimsPrincipalExtensions
{
    public static string? GetDisplayName(this ClaimsPrincipal principal) {
        return principal.FindFirst(ClaimTypes.Name)?.Value;
    }

    public static long GetUserId(this ClaimsPrincipal principal) {
        return long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    }
}
