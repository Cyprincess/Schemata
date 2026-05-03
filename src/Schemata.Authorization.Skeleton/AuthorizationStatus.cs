namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Outcome of an authorization pipeline step, directing the caller to take the corresponding action.
/// </summary>
public enum AuthorizationStatus
{
    /// <summary>The principal is authenticated; the pipeline may proceed with token issuance.</summary>
    SignIn,

    /// <summary>The caller must redirect the user agent to <see cref="AuthorizationResult.RedirectUri" />.</summary>
    Redirect,

    /// <summary>The caller must return <see cref="AuthorizationResult.Data" /> as the response body.</summary>
    Content,

    /// <summary>The caller must issue an authentication challenge (e.g. WWW-Authenticate header).</summary>
    Challenge,
}
