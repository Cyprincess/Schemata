using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation;

/// <summary>
///     Fluent builder for configuring multi-tenancy resolvers and per-tenant services.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
public sealed class SchemataTenancyBuilder<TTenant, TKey>
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataTenancyBuilder{TTenant, TKey}" /> class.
    /// </summary>
    public SchemataTenancyBuilder(IServiceCollection services) { Services = services; }

    /// <summary>Gets the service collection for registering tenant-related services.</summary>
    public IServiceCollection Services { get; }
}
