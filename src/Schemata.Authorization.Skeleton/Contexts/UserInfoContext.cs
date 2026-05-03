using System.Collections.Generic;
using System.Security.Claims;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the UserInfo endpoint pipeline.
///     Consumed by <see cref="Advisors.IUserInfoAdvisor" />.
/// </summary>
public sealed class UserInfoContext
{
    /// <summary>Authenticated principal from the access token.</summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>Internal (public) subject identifier before application-specific resolution.</summary>
    public string? InternalSubject { get; set; }

    /// <summary>Scopes granted to the access token. Advisors check scopes before including claims.</summary>
    public HashSet<string> GrantedScopes { get; set; } = new();

    /// <summary>
    ///     Whether the access token was issued to the end-user (as opposed to a machine client via token exchange).
    ///     Some claims should only be returned for end-user tokens.
    /// </summary>
    public bool IsEndUserToken { get; set; }
}
