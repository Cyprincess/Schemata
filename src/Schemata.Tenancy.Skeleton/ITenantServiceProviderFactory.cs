using System;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

public interface ITenantServiceProviderFactory<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    IServiceProvider CreateServiceProvider(ITenantContextAccessor<TTenant, TKey> accessor);
}
