using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class RegisterRequest
{
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    [Phone]
    public virtual string? PhoneNumber { get; set; }

    [Required]
    public virtual string Password { get; set; } = null!;
}
