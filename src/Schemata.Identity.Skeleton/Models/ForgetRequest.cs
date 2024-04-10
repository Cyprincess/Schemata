using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ForgetRequest
{
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    [Phone]
    public virtual string? PhoneNumber { get; set; }
}
