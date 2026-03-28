using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

public sealed class AuthorizeContext<TApplication>
    where TApplication : SchemataApplication
{
    public AuthorizeRequest? Request { get; set; }

    public TApplication? Application { get; set; }

    public ClaimsPrincipal? Principal { get; set; }

    public string? ResponseMode { get; set; }

    public ConsentDecision ConsentDecision { get; set; }

    public bool RequireReauthentication { get; set; }
}
