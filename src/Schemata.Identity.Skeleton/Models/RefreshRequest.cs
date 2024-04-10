using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class RefreshRequest
{
    [Required]
    public virtual string RefreshToken { get; set; } = null!;
}
