namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Consent state for an authorization request, evaluated by the authorization pipeline's consent advisors.
/// </summary>
public enum ConsentDecision
{
    /// <summary>The consent check has not run yet.</summary>
    Pending,

    /// <summary>Consent was previously granted; the request can be auto-approved.</summary>
    Granted,

    /// <summary>Consent is required; the user must be shown a consent prompt.</summary>
    Required,

    /// <summary>Consent was explicitly denied; the request must be rejected.</summary>
    Denied,
}
