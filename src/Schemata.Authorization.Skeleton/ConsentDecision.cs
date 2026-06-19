namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Consent state for an authorization request, evaluated by the authorization pipeline's consent advisors.
/// </summary>
public enum ConsentDecision
{
    /// <summary>The consent decision awaits evaluation.</summary>
    Pending,

    /// <summary>Existing consent allows the request to be auto-approved.</summary>
    Granted,

    /// <summary>Consent is required; the user must be shown a consent prompt.</summary>
    Required,

    /// <summary>The resource owner denied consent; the request must be rejected.</summary>
    Denied,
}
