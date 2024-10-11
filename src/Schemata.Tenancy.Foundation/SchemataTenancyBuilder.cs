using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation;

public sealed class SchemataTenancyBuilder<TTenant, TKey> where TTenant : SchemataTenant<TKey>
                                                          where TKey : struct, IEquatable<TKey>
{
    public SchemataTenancyBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }
}
