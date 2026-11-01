using System.Collections.Generic;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Core.Building;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation;

/// <summary>
///     Registers the host-selected authorization entity set as management resources for a
///     transport feature.
/// </summary>
public interface IAuthorizationResourceRegistration
{
    /// <summary>
    ///     Registers the application, scope, and token management resources on
    ///     <paramref name="resources" /> for the given endpoint names.
    /// </summary>
    /// <param name="resources">The transport resource builder.</param>
    /// <param name="endpoints">The endpoint names the transport exposes.</param>
    void Register(SchemataResourceBuilder resources, IList<string> endpoints);
}

internal sealed class AuthorizationResourceRegistration<TApp, TScope, TToken>
    : IAuthorizationResourceRegistration
    where TApp : SchemataApplication, new()
    where TScope : SchemataScope
    where TToken : SchemataToken, new()
{
    public void Register(SchemataResourceBuilder resources, IList<string> endpoints) {
        resources.Use<TApp, ApplicationRequest, ApplicationDetail, ApplicationSummary>(endpoints);
        resources.Use<TScope, ScopeRequest, ScopeDetail, ScopeSummary>(endpoints);
        resources.Use<TToken, TToken, TToken, TToken>(endpoints);
    }
}
