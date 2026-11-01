using System;

namespace Schemata.Tenancy.Skeleton.Tenancy;

public readonly record struct TenantIdentity(Guid? Uid)
{
    public static TenantIdentity Host => default;
}
