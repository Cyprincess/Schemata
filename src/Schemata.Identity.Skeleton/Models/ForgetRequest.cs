using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Skeleton.Models;

/// <summary>
///     Request model for initiating a forgotten-password or verification code flow.
/// </summary>
public class ForgetRequest
{
    /// <summary>
    ///     Gets or sets the email address to send the reset code to.
    /// </summary>
    [EmailAddress]
    public virtual string? EmailAddress { get; set; }

    /// <summary>
    ///     Gets or sets the phone number to send the reset code to.
    /// </summary>
    [Phone]
    public virtual string? PhoneNumber { get; set; }
}
