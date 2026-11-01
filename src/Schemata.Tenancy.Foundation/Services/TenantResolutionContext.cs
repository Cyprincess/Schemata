using System;

namespace Schemata.Tenancy.Foundation.Services;

internal sealed class TenantResolutionContext
{
    internal required IServiceProvider Services { get; set; }
}
