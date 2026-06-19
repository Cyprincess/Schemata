using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Password reset code request body.
/// </summary>
public class ForgetRequest
{
    /// <summary>Email address that receives the password reset code.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number that receives the password reset code.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }
}
