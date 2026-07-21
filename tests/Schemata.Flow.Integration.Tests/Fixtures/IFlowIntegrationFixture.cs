using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Advisors;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Schemata.Resource.Foundation;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public interface IFlowIntegrationFixture
{
    IServiceScope CreateScope();
}

internal static class FlowFixtureServices
{
    internal static void AddResourceTypeResolver(IServiceCollection services, params Type[] resourceTypes) {
        services.Configure<SchemataResourceOptions>(options => {
            foreach (var resourceType in resourceTypes) {
                options.Resources[resourceType.TypeHandle] = new(resourceType);
            }
        });
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
    }

    internal static void AddFlowServices(IServiceCollection services) {
        services.AddLogging();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddIdentifier<>)));
        services.TryAddSingleton<IProcessRegistry, ProcessRegistry>();
        services.TryAddSingleton<ProcessPersistence>();
        services.TryAddScoped<ProcessLifecycleNotifier>();
        services.TryAddScoped<FlowRunner>();
        services.TryAddScoped<IFlowRunner>(sp => sp.GetRequiredService<FlowRunner>());
        services.TryAddScoped<CorrelateMessageHandler>();
        services.TryAddScoped<ThrowSignalHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IFlowSourceAdvisor<>), typeof(AdviceSourceProjection<>)));
        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(SchemataConstants.FlowEngines.Bpmn);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());
    }

    internal static async Task RegisterProcessesAsync(IServiceProvider services) {
        var registry = services.GetRequiredService<IProcessRegistry>();
        await registry.RegisterAsync<PersistTaskMutationProcess>();
        await registry.RegisterAsync<ProjectionProcess>();
        await registry.RegisterAsync<ConditionProcess>();
        await registry.RegisterAsync<FailingTaskProcess>();
        await registry.RegisterAsync<BranchWriteProcess>();
        await registry.RegisterAsync<IdempotencyProcess>();
        await registry.RegisterAsync<CompensationReloadProcess>(SchemataConstants.FlowEngines.Bpmn);
        await registry.RegisterAsync<CompensationTerminalProcess>(SchemataConstants.FlowEngines.Bpmn);
    }
}
