using System;
using Schemata.Core;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation;

public sealed class SchemataTenancyBuilder<TTenant, TKey>(Services services)
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    public Services Services => services;
}
