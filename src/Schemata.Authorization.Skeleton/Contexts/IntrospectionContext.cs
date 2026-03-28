using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

public sealed class IntrospectionContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    public TApplication? Application { get; set; }

    public IntrospectRequest? Request { get; set; }

    public TToken? Token { get; set; }

    public ClaimsPrincipal? Principal { get; set; }

    public IntrospectionResponse? Response { get; set; }
}
