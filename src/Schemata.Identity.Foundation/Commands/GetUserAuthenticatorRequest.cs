using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Commands;

/// <summary>Requests an identity user's authenticator enrollment state.</summary>
public sealed record GetUserAuthenticatorRequest<TUser>(ClaimsPrincipal? Principal)
    : ICommand<IdentityResult<AuthenticatorResponse>>, IRequestPrincipal
    where TUser : SchemataUser, new()
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
