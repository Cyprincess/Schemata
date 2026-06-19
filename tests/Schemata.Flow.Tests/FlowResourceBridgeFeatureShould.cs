using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Features;
using Schemata.Flow.Grpc.Features;
using Schemata.Flow.Http.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Resource.Foundation;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowResourceBridgeFeatureShould
{
    [Fact]
    public void RegisterHttpResourcesHandlersAndMethods_WhenHttpBridgeIsInstalled() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowHttpFeature(), services);

        Assert.Contains(services, d => d.ServiceType == typeof(StartProcessHandler));
        AssertHandlersRegistered(services);
        AssertFlowResources(BuildResourceOptions(services), HttpResourceAttribute.Name);
    }

    [Fact]
    public void RegisterGrpcResourcesHandlersAndMethods_WhenGrpcBridgeIsInstalled() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowGrpcFeature(), services);

        Assert.Contains(services, d => d.ServiceType == typeof(StartProcessHandler));
        AssertHandlersRegistered(services);
        AssertFlowResources(BuildResourceOptions(services), GrpcResourceAttribute.Name);
    }

    [Fact]
    public void RegisterStartAndSignalRequests_ThroughResourceMethods() {
        var services = new ServiceCollection();

        Configure(new SchemataFlowGrpcFeature(), services);

        var methods = BuildResourceOptions(services).Methods[typeof(SchemataProcess).TypeHandle];

        Assert.Contains(
            methods,
            m => m.Handler == typeof(StartProcessHandler)
              && HandlerRequest(m.Handler) == typeof(StartProcessInstanceRequest));
        Assert.Contains(
            methods,
            m => m.Handler == typeof(ThrowSignalHandler) && HandlerRequest(m.Handler) == typeof(ThrowSignalRequest));
    }

    private static void AssertHandlersRegistered(IServiceCollection services) {
        Assert.Contains(services, d => d.ServiceType == typeof(StartProcessHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(CompleteActivityHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(CorrelateMessageHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(ThrowSignalHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(TerminateProcessHandler));
    }

    private static void AssertFlowResources(SchemataResourceOptions options, string endpoint) {
        var process = options.Resources[typeof(SchemataProcess).TypeHandle];
        Assert.Equal(typeof(SchemataProcess), process.Entity);
        Assert.Equal(typeof(SchemataProcess), process.Request);
        Assert.Equal(typeof(SchemataProcess), process.Detail);
        Assert.Equal(typeof(SchemataProcess), process.Summary);
        Assert.Equal([endpoint], process.Endpoints);
        Assert.Equal([Operations.Get, Operations.List], process.Operations);

        var processMethods = options.Methods[typeof(SchemataProcess).TypeHandle].OrderBy(m => m.Verb).ToArray();
        Assert.Equal(5, processMethods.Length);
        AssertMethod(processMethods, "complete", typeof(CompleteActivityHandler), ResourceMethodScope.Instance);
        AssertMethod(processMethods, "correlate", typeof(CorrelateMessageHandler), ResourceMethodScope.Instance);
        AssertMethod(processMethods, "signal", typeof(ThrowSignalHandler), ResourceMethodScope.Collection);
        AssertMethod(processMethods, "start", typeof(StartProcessHandler), ResourceMethodScope.Collection);
        AssertMethod(processMethods, "terminate", typeof(TerminateProcessHandler), ResourceMethodScope.Instance);

        var transition = options.Resources[typeof(SchemataProcessTransition).TypeHandle];
        Assert.Equal(typeof(SchemataProcessTransition), transition.Entity);
        Assert.Equal(typeof(SchemataProcessTransition), transition.Request);
        Assert.Equal(typeof(SchemataProcessTransition), transition.Detail);
        Assert.Equal(typeof(SchemataProcessTransition), transition.Summary);
        Assert.Equal([endpoint], transition.Endpoints);
        Assert.Equal([Operations.Get, Operations.List], transition.Operations);
    }

    private static void AssertMethod(
        ResourceMethodAttribute[] methods,
        string                    verb,
        Type                      handler,
        ResourceMethodScope       scope
    ) {
        var method = methods.Single(m => m.Verb == verb);
        Assert.Equal(handler, method.Handler);
        Assert.Equal(scope, method.Scope);
    }

    private static Type HandlerRequest(Type handler) {
        return handler.GetInterfaces()
                      .Single(i => i.IsGenericType
                                && i.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>))
                      .GetGenericArguments()[1];
    }

    private static SchemataResourceOptions BuildResourceOptions(IServiceCollection services) {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;
    }

    private static void Configure(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(),
                                  Mock.Of<IWebHostEnvironment>());
    }
}
