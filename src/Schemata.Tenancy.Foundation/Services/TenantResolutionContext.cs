using System;

namespace Schemata.Tenancy.Foundation.Services;

internal sealed class TenantResolutionContext
{
    internal IServiceProvider Services { get; set; } = null!;
}
