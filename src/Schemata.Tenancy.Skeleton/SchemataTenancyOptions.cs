using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Skeleton;

/// <summary>
///     Options controlling tenant service provider caching and per-tenant DI overrides.
/// </summary>
public sealed class SchemataTenancyOptions
{
    /// <summary>Default maximum number of per-tenant service providers held by the cache.</summary>
    public const int DefaultMaxCapacity = 1000;

    /// <summary>Default sliding expiration applied to cached per-tenant service providers.</summary>
    public static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets the sliding expiration applied to each cached per-tenant provider.</summary>
    public TimeSpan ProviderSlidingExpiration { get; set; } = DefaultSlidingExpiration;

    /// <summary>Gets or sets the maximum number of per-tenant providers retained in the cache.</summary>
    public int ProviderMaxCapacity { get; set; } = DefaultMaxCapacity;

    /// <summary>
    ///     Overrides applied to every tenant container (and the no-tenant root container) in registration order.
    /// </summary>
    public List<Action<IServiceCollection>> AllOverrides { get; } = [];

    /// <summary>
    ///     Overrides keyed by tenant identifier applied after <see cref="AllOverrides" /> for matching tenants only.
    /// </summary>
    public Dictionary<string, List<Action<IServiceCollection>>> TenantOverrides { get; } = [];

    /// <summary>
    ///     Dynamic overrides invoked once per tenant container with the tenant id and the root service provider;
    ///     applied after both <see cref="AllOverrides" /> and <see cref="TenantOverrides" />.
    /// </summary>
    public List<Action<string, IServiceCollection, IServiceProvider>> DynamicOverrides { get; } = [];
}
