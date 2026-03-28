using System.Security.Claims;

// ReSharper disable once CheckNamespace
namespace System;

/// <summary>
///     Extension methods for extracting identity information from a <see cref="ClaimsPrincipal" />.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    ///     Gets the display name from the <see cref="ClaimTypes.Name" /> claim.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <returns>The display name, or <see langword="null" /> if the claim is absent.</returns>
    public static string? GetDisplayName(this ClaimsPrincipal principal) {
        return principal.FindFirst(ClaimTypes.Name)?.Value;
    }

    /// <summary>
    ///     Gets the user identifier from the <see cref="ClaimTypes.NameIdentifier" /> claim.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <returns>The user identifier, or <c>0</c> if the claim is absent or not a valid number.</returns>
    public static long GetUserId(this ClaimsPrincipal principal) {
        return long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    }
}
