using System;
using Microsoft.AspNetCore.Http;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Declares that one authentication scheme is owned by the scope that registered the
///     native <see cref="Microsoft.AspNetCore.Authentication.IAuthenticationService" /> under
///     <see cref="ServiceKey" />. The key is a registration identity — a host or a tenant/
///     client state profile.
/// </summary>
/// <param name="Scheme">The logical authentication scheme name.</param>
/// <param name="ServiceKey">The keyed registration of the native service inside the owning scope.</param>
public sealed record AuthenticationSchemeScope(string Scheme, string ServiceKey);

/// <summary>
///     The provider and keyed registration of the native service that own one authentication
///     scheme on the current request.
/// </summary>
/// <param name="Services">The provider owning the scheme.</param>
/// <param name="ServiceKey">The keyed registration of the native service inside <paramref name="Services" />.</param>
public sealed record AuthenticationScope(IServiceProvider Services, string ServiceKey);

/// <summary>
///     Resolves the explicit scope that owns a scheme on the current request. A scope must be
///     selected per operation from immutable request state: host-owned schemes resolve to the
///     preserved host request scope, schemes owned by a state profile resolve to that profile's
///     bound scope. An operation whose owning scope is not bound fails here instead of falling
///     back to another scope's handler.
/// </summary>
public interface IAuthenticationScopeProvider
{
    /// <summary>Returns the provider and keyed native service bound to <paramref name="scheme" />.</summary>
    /// <param name="context">The current request context.</param>
    /// <param name="scheme">The scheme being operated on; never <see langword="null" />.</param>
    /// <returns>The scope owning <paramref name="scheme" />.</returns>
    AuthenticationScope GetScope(HttpContext context, string scheme);
}
