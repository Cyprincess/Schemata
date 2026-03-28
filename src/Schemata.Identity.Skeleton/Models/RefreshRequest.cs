using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class RefreshRequest
{
    /// <summary>Opaque refresh token obtained from a previous authentication.</summary>
    [Required]
    public string RefreshToken { get; set; } = null!;
}
