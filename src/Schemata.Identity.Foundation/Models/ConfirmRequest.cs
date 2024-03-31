using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class ConfirmRequest
{
    [EmailAddress]
    public string? EmailAddress { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }

    [Required]
    public string Code { get; init; } = null!;
}
