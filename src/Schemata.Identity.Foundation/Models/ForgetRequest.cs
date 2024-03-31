using System.ComponentModel.DataAnnotations;

namespace Schemata.Identity.Foundation.Models;

public class ForgetRequest
{
    [EmailAddress]
    public string? EmailAddress { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }
}
