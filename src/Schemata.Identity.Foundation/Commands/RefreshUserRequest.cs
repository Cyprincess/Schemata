using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Commands;

/// <summary>Requests refreshing an identity user's sign-in principal.</summary>
public sealed record RefreshUserRequest<TUser>(
    [property: JsonIgnore] AuthenticationTicket? Ticket,
    ClaimsPrincipal? Principal
) : ICommand<IdentityResult<ClaimsPrincipal>>, IRequestPrincipal
    where TUser : SchemataUser, new()
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
