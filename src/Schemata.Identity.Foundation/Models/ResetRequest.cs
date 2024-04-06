using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class ResetRequest
{
    [EmailAddress]
    public string? EmailAddress { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }

    [Required]
    public string Code { get; init; } = null!;

    [Required]
    public string Password { get; init; } = null!;
}
