using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public interface IFlowIntegrationFixture
{
    IServiceScope CreateScope();
}

internal static class FlowFixtureServices
{
    internal static void AddFlowServices(IServiceCollection services) {
        services.AddLogging();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddIdentifier<>)));
        services.TryAddSingleton<IProcessRegistry, ProcessRegistry>();
        services.TryAddSingleton<ProcessPersistence>();
        services.TryAddSingleton<FlowProcessAuthorization>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IFlowSourceAdvisor<>), typeof(AdviceSourceProjection<>)));
        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
    }

    internal static async Task RegisterProcessesAsync(IServiceProvider services) {
        var registry = services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<PersistTaskMutationProcess>();
        await registry.RegisterAsync<ProjectionProcess>();
        await registry.RegisterAsync<ConditionProcess>();
        await registry.RegisterAsync<FailingTaskProcess>();
        await registry.RegisterAsync<BranchWriteProcess>();
    }
}
