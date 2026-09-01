using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Commands;

/// <summary>Requests authentication of an identity user.</summary>
public sealed record LoginUserRequest<TUser>(LoginRequest Request, ClaimsPrincipal? Principal)
    : ICommand<IdentityResult<ClaimsPrincipal>>, IRequestPrincipal
    where TUser : SchemataUser, new()
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
