using System.Security.Claims;
using System.Text.Json.Serialization;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Queries;

/// <summary>Queries an identity user's profile claims.</summary>
public sealed record GetUserProfileQuery<TUser>(ClaimsPrincipal? Principal)
    : IQuery<IdentityResult<ClaimsStore>>, IRequestPrincipal
    where TUser : SchemataUser, new()
{
    [JsonIgnore]
    public ClaimsPrincipal? Principal { get; set; } = Principal;
}
