using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Payload of an authorization-code token: the granted request plus the trusted grant
///     context resolved from the original authentication event. A code exchange inherits the
///     canonical subject/category, effective scope/profile, optional session, and optional
///     <c>acr</c>/<c>amr</c>/<c>auth_time</c> evidence without resolving the provider again.
/// </summary>
internal sealed class AuthorizationCodePayload
{
    public AuthorizeRequest? Request { get; set; }

    /// <summary>Trusted grant lineage resolved at the original authentication event.</summary>
    public AuthorizationGrantContext? Grant { get; set; }
}
