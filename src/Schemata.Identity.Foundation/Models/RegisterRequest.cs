using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class RegisterRequest
{
    [EmailAddress]
    public string? EmailAddress { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }

    [Required]
    public string Password { get; init; } = null!;
}
