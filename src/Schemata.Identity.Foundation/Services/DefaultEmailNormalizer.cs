using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Services;

/// <summary>Returns the culture-invariant uppercase email lookup key.</summary>
public sealed class DefaultEmailNormalizer : IEmailNormalizer
{
    public string NormalizeEmail(string email) => email.ToUpperInvariant();
}
