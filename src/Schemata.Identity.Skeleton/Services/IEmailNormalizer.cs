namespace Schemata.Identity.Skeleton.Services;

/// <summary>Normalizes an email address for identity storage and lookup.</summary>
public interface IEmailNormalizer
{
    string NormalizeEmail(string email);
}
