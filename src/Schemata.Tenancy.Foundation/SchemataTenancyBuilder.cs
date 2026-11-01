using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Tenancy.Foundation.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation;

/// <summary>
///     Fluent builder for configuring multi-tenancy resolvers and per-tenant services.
/// </summary>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed class SchemataTenancyBuilder<TTenant>
    where TTenant : SchemataTenant
{
    /// <summary>Creates a builder over the application's service collection.</summary>
    public SchemataTenancyBuilder(IServiceCollection services) { Services = services; }

    /// <summary>Gets the service collection for registering tenant-related services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Registers the tenant repository advisors for every entity type and composes per-tenant
    ///     repository registrations through the dynamic tenant overrides. Each tenant container
    ///     receives a verified <see cref="TenantRepositoryBinding" /> and a tenant-namespaced
    ///     <see cref="RepositoryBinding" /> before <paramref name="configure" /> runs. The host
    ///     container keeps <see cref="TenantRepositoryBinding.Host" />, so host repositories see
    ///     only host rows.
    /// </summary>
    /// <remarks>
    ///     Advisors apply to entities implementing <see cref="Schemata.Abstractions.Entities.ITenant" /> and return
    ///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Continue" /> for every other
    ///     entity. Repository implementations and their native sources are registered by the
    ///     application, explicitly per tenant inside <paramref name="configure" />. Register each
    ///     entity's closed <c>IResourceMutation&lt;TEntity&gt;</c> alongside its repository; a
    ///     tenant without that registration resolves the host mutation and cannot join a tenant
    ///     unit of work.
    /// </remarks>
    public SchemataTenancyBuilder<TTenant> UseRepositories(Action<TenantRepositoryBuilder<TTenant>> configure) {
        ArgumentNullException.ThrowIfNull(configure);

        Services.TryAddSingleton(TenantRepositoryBinding.Host);
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddTenant<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryBuildQueryAdvisor<>), typeof(AdviceBuildQueryTenant<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryUpdateAdvisor<>), typeof(AdviceUpdateTenant<>)));
        Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryRemoveAdvisor<>), typeof(AdviceRemoveTenant<>)));

        Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((identifier, services, _) => {
            if (!Guid.TryParse(identifier, out var uid) || uid == Guid.Empty) {
                throw new InvalidOperationException($"Tenant repositories require a non-empty verified tenant identifier; tenant '{identifier}' has none.");
            }
            services.TryAddSingleton(new RepositoryBinding($"tenant:{uid:n}"));
            services.TryAddSingleton(new TenantRepositoryBinding(uid));
            configure(new(services, uid));
        }));
        return this;
    }
}
