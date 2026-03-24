using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Creates service scopes from the tenant-isolated service provider.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
/// <typeparam name="TKey">The tenant identifier type.</typeparam>
/// <remarks>
///     When a tenant is resolved, scopes are created from the tenant's isolated container.
///     When no tenant is resolved, scopes fall back to the root provider.
/// </remarks>
public interface ITenantServiceScopeFactory<TTenant, TKey> : IServiceScopeFactory
    where TTenant : SchemataTenant<TKey>
    where TKey : struct, IEquatable<TKey>;
