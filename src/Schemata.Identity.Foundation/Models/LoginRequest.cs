using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class LoginRequest
{
    [Required]
    public string Username { get; init; } = null!;

    [Required]
    public string Password { get; init; } = null!;

    public string? TwoFactorCode { get; init; }

    public string? TwoFactorRecoveryCode { get; init; }
}
