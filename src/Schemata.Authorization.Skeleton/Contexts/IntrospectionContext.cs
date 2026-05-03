using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the introspection endpoint pipeline.
///     Consumed by <see cref="Advisors.IIntrospectionAdvisor{TApplication, TToken}" />.
/// </summary>
public sealed class IntrospectionContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>Resolved client application performing the introspection.</summary>
    public TApplication? Application { get; set; }

    /// <summary>Introspection request.</summary>
    public IntrospectRequest? Request { get; set; }

    /// <summary>The token entity found by resolving the token from the request.</summary>
    public TToken? Token { get; set; }

    /// <summary>Claims principal derived from the resolved token.</summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>Response being built; advisors populate additional claims.</summary>
    public IntrospectionResponse? Response { get; set; }
}
