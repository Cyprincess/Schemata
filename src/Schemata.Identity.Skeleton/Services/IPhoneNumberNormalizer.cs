namespace Schemata.Identity.Skeleton.Services;

/// <summary>Canonicalizes a complete E.164 phone number.</summary>
public interface IPhoneNumberNormalizer
{
    string NormalizePhoneNumber(string fullE164);
}
