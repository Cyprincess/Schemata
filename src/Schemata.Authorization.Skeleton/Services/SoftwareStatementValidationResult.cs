using System.Collections.Generic;
using System.Text.Json;

namespace Schemata.Authorization.Skeleton.Services;

public sealed class SoftwareStatementValidationResult
{
    private SoftwareStatementValidationResult(bool valid, IDictionary<string, JsonElement>? claims) {
        IsValid = valid;
        Claims = claims;
    }

    public bool IsValid { get; }
    public IDictionary<string, JsonElement>? Claims { get; }

    public static SoftwareStatementValidationResult Invalid { get; } = new(false, null);
    public static SoftwareStatementValidationResult Unapproved { get; } = new(true, null);

    public static SoftwareStatementValidationResult Approved(IDictionary<string, JsonElement> claims) {
        System.ArgumentNullException.ThrowIfNull(claims);
        return new(true, claims);
    }
}
