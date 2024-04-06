using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class ProfileRequest
{
    [EmailAddress]
    public string? EmailAddress { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }

    public string? OldPassword { get; init; }

    public string? NewPassword { get; init; }
}
