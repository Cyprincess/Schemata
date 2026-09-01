using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Abstractions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Commands;

/// <summary>Requests enrollment of an identity user's authenticator.</summary>
public sealed record EnrollUserAuthenticatorRequest<TUser>(AuthenticatorRequest Request, ClaimsPrincipal? Principal)
    : ICommand<IdentityResult<Unit>>, IRequestPrincipal
    where TUser : SchemataUser, new()
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
