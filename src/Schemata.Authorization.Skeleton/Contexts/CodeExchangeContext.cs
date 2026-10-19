using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the authorization code exchange pipeline.
///     Populated after pre-validation and consumed by <see cref="Advisors.ICodeExchangeAdvisor{TApplication}" />.
/// </summary>
public sealed class CodeExchangeContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Token endpoint request.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The authorization code token entity, found by resolving the code from the request.</summary>
    public SchemataToken? CodeToken { get; set; }

    /// <summary>Deserialized payload from the authorization code token, containing the original authorize request.</summary>
    public AuthorizeRequest? Payload { get; set; }
    public string? AuthorizationDetails { get; set; }

    /// <summary>
    ///     Whether the authorization code must be single-use. Defaults to <c>true</c>.
    ///     Extension grants may set this to <c>false</c> to allow repeated use.
    /// </summary>
    public bool RequireSingleUse { get; set; } = true;

    /// <summary>
    ///     The narrowed authorization grant established by the handler before the exchange runs;
    ///     device-secret advisors read its profile, scope, and family state from here.
    /// </summary>
    public AuthorizationGrantContext? Grant { get; set; }

    /// <summary>The DPoP proof key binding validated at the request stage.</summary>
    public DpopBinding? Dpop { get; set; }

    /// <summary>The device secret issued or reused by the device SSO advisor.</summary>
    public DeviceSecretIssuance? DeviceSecret { get; set; }

    /// <summary>
    ///     The response written by an advisor returning
    ///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle" />.
    /// </summary>
    public AuthorizationResult? Result { get; set; }
}
