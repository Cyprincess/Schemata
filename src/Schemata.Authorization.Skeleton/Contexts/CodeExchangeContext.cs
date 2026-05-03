using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the authorization code exchange pipeline.
///     Populated after pre-validation and consumed by <see cref="Advisors.ICodeExchangeAdvisor{TApplication, TToken}" />.
/// </summary>
public sealed class CodeExchangeContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>Token endpoint request.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The authorization code token entity, found by resolving the code from the request.</summary>
    public TToken? CodeToken { get; set; }

    /// <summary>Deserialized payload from the authorization code token, containing the original authorize request.</summary>
    public AuthorizeRequest? Payload { get; set; }

    /// <summary>
    ///     Whether the authorization code must be single-use. Defaults to <c>true</c>.
    ///     Extension grants may set this to <c>false</c> to allow repeated use.
    /// </summary>
    public bool RequireSingleUse { get; set; } = true;
}
