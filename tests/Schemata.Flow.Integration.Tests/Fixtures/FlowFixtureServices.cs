using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Repository;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Foundation;
using Schemata.Flow.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Schemata.Resource.Foundation;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Flow.Integration.Tests.Fixtures;

internal static class FlowFixtureServices
{
    internal static void AddResourceTypeResolver(IServiceCollection services, params Type[] resourceTypes) {
        var registry = new ResourceRegistry();
        foreach (var resourceType in resourceTypes) {
            registry.Add(new(resourceType), []);
        }

        services.TryAddSingleton<ResourceRegistry>(registry);
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
    }

    internal static void AddFlowServices(IServiceCollection services) {
        AddFlowServices<SchemataProcess, SchemataProcessToken, SchemataProcessTransition,
            SchemataProcessCompensation, SchemataProcessParticipant, SchemataProcessSource,
            SchemataFlowEffectIntent>(services);
    }

    internal static void AddFlowServices<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(IServiceCollection services)
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new() {
        AddFlowServices<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, SchemataFlowEffectIntent>(services);
    }

    internal static void AddFlowServices<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource, TEffectIntent>(IServiceCollection services)
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new()
        where TEffectIntent : SchemataFlowEffectIntent, new() {
        Schemata.Flow.Tests.FlowTestCreation.Register(services);
        services.AddLogging();
        services.AddSchemataFlow<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>();
        services.AddSchemataFlowRepositoryEffectRecorder<TEffectIntent>();
        services.TryAddKeyedSingleton<IFlowRuntime<TProcess, TToken, TTransition>, Schemata.Flow.StateMachine.StateMachineEngine<TProcess, TToken, TTransition>>(FlowConstants.Engines.StateMachine);
        services.TryAddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine,
            (sp, _) => sp.GetRequiredKeyedService<IFlowRuntime<TProcess, TToken, TTransition>>(FlowConstants.Engines.StateMachine));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
        services.TryAddKeyedSingleton<IFlowRuntime<TProcess, TToken, TTransition>, Schemata.Flow.Bpmn.BpmnEngine<TProcess, TToken, TTransition>>(FlowConstants.Engines.Bpmn);
        services.TryAddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.Bpmn,
            (sp, _) => sp.GetRequiredKeyedService<IFlowRuntime<TProcess, TToken, TTransition>>(FlowConstants.Engines.Bpmn));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());
    }

    internal static void AddTenantFlowRepositories(IServiceCollection services, string dbPath) {
        services.AddDbContextFactory<TenantFlowDbContext>(options => options.UseSqlite($"Data Source={dbPath}")
                                                                          .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<TenantProcess, EfCoreRepository<TenantFlowDbContext, TenantProcess>>();
        services.AddRepository<TenantProcessToken, EfCoreRepository<TenantFlowDbContext, TenantProcessToken>>();
        services.AddRepository<TenantProcessTransition, EfCoreRepository<TenantFlowDbContext, TenantProcessTransition>>();
        services.AddRepository<TenantProcessCompensation, EfCoreRepository<TenantFlowDbContext, TenantProcessCompensation>>();
        services.AddRepository<TenantProcessParticipant, EfCoreRepository<TenantFlowDbContext, TenantProcessParticipant>>();
        services.AddRepository<TenantProcessSource, EfCoreRepository<TenantFlowDbContext, TenantProcessSource>>();
        services.AddRepository<TenantFlowEffectIntent, EfCoreRepository<TenantFlowDbContext, TenantFlowEffectIntent>>();
        services.AddScoped<IUnitOfWork<TenantFlowDbContext>, EfCoreUnitOfWork<TenantFlowDbContext>>();
    }

    internal static void AddTenantFlowRepositories(IServiceCollection services, DataOptions options) {
        services.TryAddScoped(_ => new TestDataConnection(options));
        services.TryAddSingleton<Func<TestDataConnection>>(_ => () => new(options));
        services.AddRepository<TenantProcess, LinqToDbRepository<TestDataConnection, TenantProcess>>();
        services.AddRepository<TenantProcessToken, LinqToDbRepository<TestDataConnection, TenantProcessToken>>();
        services.AddRepository<TenantProcessTransition, LinqToDbRepository<TestDataConnection, TenantProcessTransition>>();
        services.AddRepository<TenantProcessCompensation, LinqToDbRepository<TestDataConnection, TenantProcessCompensation>>();
        services.AddRepository<TenantProcessParticipant, LinqToDbRepository<TestDataConnection, TenantProcessParticipant>>();
        services.AddRepository<TenantProcessSource, LinqToDbRepository<TestDataConnection, TenantProcessSource>>();
        services.AddRepository<TenantFlowEffectIntent, LinqToDbRepository<TestDataConnection, TenantFlowEffectIntent>>();
        services.AddScoped<IUnitOfWork<TestDataConnection>, LinqToDbUnitOfWork<TestDataConnection>>();
    }

    /// <summary>
    ///     Resolves every requested identifier as an existing tenant so tests can enter arbitrary
    ///     partitions without seeding a catalog, then composes the tenant repository registration
    ///     through <paramref name="configure" />. One tenant row per identifier keeps the version
    ///     stamp stable across resolutions so the provider cache stays valid.
    /// </summary>
    internal static void AddTenantRepositories<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
        IServiceCollection                                services,
        Action<TenantRepositoryBuilder<SchemataTenant>> configure
    )
        where TProcess : SchemataProcess, new()
        where TToken : SchemataProcessToken, new()
        where TTransition : SchemataProcessTransition, new()
        where TCompensation : SchemataProcessCompensation, new()
        where TParticipant : SchemataProcessParticipant, new()
        where TSource : SchemataProcessSource, new() {
        var tenants = new ConcurrentDictionary<Guid, SchemataTenant>();
        var manager = new Mock<ITenantManager<SchemataTenant>>();
        manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                tenants.GetOrAdd(identifier, static value => new SchemataTenant { Uid = value, Timestamp = Guid.NewGuid(), Name = value.ToString() })));
        services.AddSingleton<ITenantManager<SchemataTenant>>(manager.Object);
        var tenancy = new SchemataTenancyBuilder<SchemataTenant>(services);
        tenancy.UseRepositories(configure);
        tenancy.UseRepositories(tenant => {
            tenant.Services.AddFlowExecution<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>();
            // Flow authorization is opt-in on the host; the tenant execution graph mirrors it.
            if (services.Any(descriptor => descriptor.ServiceType == typeof(FlowAccessPolicy<TProcess, TToken, TParticipant>))) {
                tenant.Services.AddFlowAccessExecution<TProcess, TToken, TParticipant>();
            }
        });
    }

    internal static (MemoryCacheTenantProviderCache Cache, SchemataTenantServiceScopeFactory<SchemataTenant> Scopes) CreateTenantScopes(ServiceProvider root) {
        var tenancy   = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
        var cache     = new MemoryCacheTenantProviderCache(tenancy);
        var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy);
        return (cache, new(root, providers));
    }

    internal static async Task RegisterProcessesAsync(IServiceProvider services) {
        var registry = services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<PersistTaskMutationProcess>();
        await registry.RegisterAsync<ProjectionProcess>();
        await registry.RegisterAsync<ConditionProcess>();
        await registry.RegisterAsync<FailingTaskProcess>();
        await registry.RegisterAsync<BranchWriteProcess>();
        await registry.RegisterAsync<FailingSavePreparationProcess>();
        await registry.RegisterAsync<IdempotencyProcess>();
        await registry.RegisterAsync<CompensationReloadProcess>(FlowConstants.Engines.Bpmn);
        await registry.RegisterAsync<CompensationTerminalProcess>(FlowConstants.Engines.Bpmn);
    }
}
