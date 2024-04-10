using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class LoginRequest
{
    [Required]
    public virtual string Username { get; set; } = null!;

    [Required]
    public virtual string Password { get; set; } = null!;

    public virtual string? TwoFactorCode { get; set; }

    public virtual string? TwoFactorRecoveryCode { get; set; }
}
