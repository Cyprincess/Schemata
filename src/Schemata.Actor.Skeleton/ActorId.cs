using Schemata.Abstractions.Tenancy;

namespace Schemata.Actor.Skeleton;

public readonly record struct ActorId(string Type, string Key)
{
    public TenantIdentity Tenant { get; init; } = TenantContext.Current;

    public override string ToString() => $"{Type}/{Key}";
}
