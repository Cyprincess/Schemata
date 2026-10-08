using System;

namespace Schemata.Abstractions.Tenancy;

public readonly record struct TenantIdentity(Guid? Uid)
{
    public static TenantIdentity Host => default;
}
