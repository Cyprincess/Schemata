using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

public sealed class RefreshTokenContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    public TokenRequest? Request { get; set; }

    public TApplication? Application { get; set; }

    public TToken? Token { get; set; }

    public ClaimsPrincipal? Principal { get; set; }
}
