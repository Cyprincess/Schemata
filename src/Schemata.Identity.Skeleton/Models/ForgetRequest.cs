using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

public class ForgetRequest
{
    /// <summary>Email address to send the password reset code to; mutually exclusive with PhoneNumber.</summary>
    [EmailAddress]
    public string? EmailAddress { get; set; }

    /// <summary>Phone number to send the password reset code to; mutually exclusive with EmailAddress.</summary>
    [Phone]
    public string? PhoneNumber { get; set; }
}
