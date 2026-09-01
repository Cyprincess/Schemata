using Schemata.Core.Building;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Bpmn;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Schemata.Resource.Foundation;

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
        services.AddLogging();
        services.AddSchemataFlow();
        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(FlowConstants.Engines.StateMachine);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(FlowConstants.Engines.Bpmn);
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
        await registry.RegisterAsync<CompensationReloadProcess>(FlowConstants.Engines.Bpmn);
        await registry.RegisterAsync<CompensationTerminalProcess>(FlowConstants.Engines.Bpmn);
    }
}