using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for refreshing an authentication token.
/// </summary>
public class RefreshRequest
{
    /// <summary>
    ///     Gets or sets the refresh token to exchange for a new access token.
    /// </summary>
    [Required]
    public virtual string RefreshToken { get; set; } = null!;
}
