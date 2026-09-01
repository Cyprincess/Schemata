using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Core.Features;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Foundation.Handlers;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Schemata.Flow.StateMachine.Features;
using Xunit;

namespace Schemata.Flow.Tests;

public sealed class SchemataFlowFeatureShould
{
    [Fact]
    public void ConfigureServices_Registers_ListProcessDefinitionsQueryHandler() {
        var services = new ServiceCollection();
        services.AddOptions<SchemataFlowOptions>();

        Configure(new SchemataFlowFeature(), services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DefaultListProcessDefinitionsHandler>(
            provider.GetRequiredService<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>());
        Assert.IsType<DefaultListProcessDefinitionsHandler>(
            provider.GetRequiredKeyedService<IRequestHandler<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(
                FlowConstants.Handlers.Default));
    }

    [Fact]
    public void FlowFeature_WithoutStateMachineFeature_LeavesRuntimeUnregistered() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowFeature(), services);

        var provider = services.BuildServiceProvider();
        var runtime  = provider.GetKeyedService<IFlowRuntime>(FlowConstants.Engines.StateMachine);

        Assert.Null(runtime);
    }

    [Fact]
    public void StateMachineFeature_RegistersRuntimeAndValidator() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowStateMachineFeature(), services);

        var provider   = services.BuildServiceProvider();
        var runtime    = provider.GetKeyedService<IFlowRuntime>(FlowConstants.Engines.StateMachine);
        var validators = provider.GetServices<IFlowEngineValidator>();

        Assert.IsType<StateMachineEngine>(runtime);
        Assert.Contains(validators, validator => validator is StateMachineFlowEngineValidator);
    }

    private static void Configure(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(
            services,
            new(),
            new(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IWebHostEnvironment>()
        );
    }
}
