using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ConfirmRequest
{
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    [Phone]
    public virtual string? PhoneNumber { get; set; }

    [Required]
    public virtual string Code { get; set; } = null!;
}
