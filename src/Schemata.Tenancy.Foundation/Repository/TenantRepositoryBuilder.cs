using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Repository;

/// <summary>
///     Fixed tenant-side dependency captured when a tenant container is composed. Advisor
///     executions resolve this identity from their scope instead of the ambient <c>TenantContext</c>.
/// </summary>
/// <param name="Uid">The verified non-empty tenant identifier owning the bound repositories;
///     <see langword="null" /> identifies the host partition.</param>
public sealed record TenantRepositoryBinding(Guid? Uid)
{
    /// <summary>Binding for the host partition (<c>TenantUid</c> null rows).</summary>
    public static readonly TenantRepositoryBinding Host = new((Guid?)null);
}

/// <summary>
///     Builder handed to <see cref="SchemataTenancyBuilder{TTenant}.UseRepositories" /> callbacks
///     while a tenant service collection is composed.
/// </summary>
/// <remarks>
///     A tenant partitions an entity by registering its repository implementation, native source,
///     and the closed <c>IResourceMutation&lt;TEntity&gt;</c> in <see cref="Services" />. Pair the
///     repository with that mutation: a tenant that registers only the repository resolves the
///     host open-generic mutation, which binds the host repository and refuses to join a tenant
///     unit of work. <see cref="TenantDatabaseExtensions" /> registers the repository and native
///     source for a dedicated database; the mutation registration remains the application's
///     responsibility in both modes.
/// </remarks>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class TenantRepositoryBuilder<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Creates a builder over the tenant service collection for a verified tenant.</summary>
    public TenantRepositoryBuilder(IServiceCollection services, Guid tenantUid) {
        Services  = services;
        TenantUid = tenantUid;
    }

    /// <summary>Gets the tenant service collection receiving per-tenant registrations.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the verified tenant identifier every registration is bound to.</summary>
    public Guid TenantUid { get; }
}
