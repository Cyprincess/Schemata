using System;

namespace Schemata.Identity.Foundation.Services;

/// <summary>Validates complete E.164 input and returns its digits without the leading plus sign.</summary>
public sealed class E164PhoneNumberNormalizer : Schemata.Identity.Skeleton.Services.IPhoneNumberNormalizer
{
    public string NormalizePhoneNumber(string fullE164) {
        if (fullE164 is null || fullE164.Length < 2 || fullE164.Length > 16
         || fullE164[0] != '+' || fullE164[1] is < '1' or > '9') {
            throw Invalid();
        }

        for (var index = 2; index < fullE164.Length; index++) {
            if (fullE164[index] is < '0' or > '9') throw Invalid();
        }

        return fullE164.Substring(1);
    }

    private static FormatException Invalid() => new("The phone number must be a complete E.164 value with at most 15 digits.");
}
