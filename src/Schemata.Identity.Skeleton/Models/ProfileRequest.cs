using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ProfileRequest
{
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    [Phone]
    public virtual string? PhoneNumber { get; set; }

    public virtual string? OldPassword { get; set; }

    public virtual string? NewPassword { get; set; }
}
