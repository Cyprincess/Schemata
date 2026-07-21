using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Core.Features;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Schemata.Flow.StateMachine.Features;
using Xunit;

namespace Schemata.Flow.Tests;

public sealed class SchemataFlowFeatureShould
{
    [Fact]
    public void ConfigureServices_Registers_ProcessDefinitionQueryService() {
        var services = new ServiceCollection();
        services.AddOptions<SchemataFlowOptions>();

        Configure(new SchemataFlowFeature(), services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ProcessDefinitionQueryService>(provider.GetRequiredService<ProcessDefinitionQueryService>());
    }

    [Fact]
    public void FlowFeature_WithoutStateMachineFeature_LeavesRuntimeUnregistered() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowFeature(), services);

        var provider = services.BuildServiceProvider();
        var runtime  = provider.GetKeyedService<IFlowRuntime>(SchemataConstants.FlowEngines.StateMachine);

        Assert.Null(runtime);
    }

    [Fact]
    public void StateMachineFeature_RegistersRuntimeAndValidator() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowStateMachineFeature(), services);

        var provider   = services.BuildServiceProvider();
        var runtime    = provider.GetKeyedService<IFlowRuntime>(SchemataConstants.FlowEngines.StateMachine);
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
