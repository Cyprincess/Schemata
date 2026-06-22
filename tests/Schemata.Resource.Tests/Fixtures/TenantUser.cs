using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Tests.Fixtures;

[CanonicalName("tenants/{tenant}/users/{user}")]
public sealed class TenantUser
{
    public string? Tenant { get; set; }

    public string? Name { get; set; }
}
