using System;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Tenant-bound state profile fixed when a tenant container is composed. It carries the
///     verified tenant identity and the browser cookie namespace owned by this profile; it is
///     not an <c>AdviceContext</c> item and holds no business payload.
/// </summary>
/// <param name="TenantUid">The verified non-empty tenant identifier owning the state profile.</param>
/// <param name="CookiePrefix">The cookie name prefix keeping this tenant's cookies distinguishable per origin.</param>
public sealed record TenantStateProfile(Guid TenantUid, string CookiePrefix);
