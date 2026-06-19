using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Refresh token request body.
/// </summary>
public class RefreshRequest
{
    /// <summary>Opaque refresh token issued for credential renewal.</summary>
    [Required]
    public string RefreshToken { get; set; } = null!;
}
