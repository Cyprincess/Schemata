using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Payload of an authorization-code token: the granted request plus the
///     authentication context resolved at approval, so a later bare code exchange can mint
///     <c>acr</c>, <c>amr</c>, and <c>auth_time</c> that stay fixed across the tokens derived
///     from one authorization response (RFC 9068 §2.2.1).
/// </summary>
internal sealed class AuthorizationCodePayload
{
    public AuthorizeRequest? Request { get; set; }

    public AuthenticationContext? Context { get; set; }
}
