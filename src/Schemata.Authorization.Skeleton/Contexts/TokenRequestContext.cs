using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the token endpoint request stage, shared by every grant handler.
///     Populated by the grant handler after client authentication and consumed by
///     <see cref="Advisors.ITokenRequestAdvisor{TApplication}" />. The transport DPoP proof rides
///     in on <see cref="Proof" />; a validated proof key binding is published on
///     <see cref="Binding" /> for the exchange stages and the response properties. A fresh
///     instance is created per dispatch, so consecutive requests never share stage state.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
public sealed class TokenRequestContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Token endpoint request.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The raw DPoP proof JWT header value, or <see langword="null" /> when absent.</summary>
    public string? Proof { get; set; }

    /// <summary>The proof key binding validated by the DPoP request advisor.</summary>
    public DpopBinding? Binding { get; set; }

    /// <summary>
    ///     The response written by an advisor returning
    ///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle" />.
    /// </summary>
    public AuthorizationResult? Result { get; set; }
}
