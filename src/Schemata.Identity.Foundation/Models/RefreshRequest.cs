using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = null!;
}
