using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Tests.Fixtures;

[CanonicalName("tenants/global/settings/{setting}")]
public sealed class GlobalSetting
{
    public string? Name { get; set; }
}
