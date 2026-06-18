using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Grpc.Features;
using Schemata.Scheduling.Http.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulingBridgeFeatureShould
{
    [Fact]
    public void RegisterNoResourceHandlers_WhenOnlyFoundationFeatureIsInstalled() {
        var services = new ServiceCollection();

        Configure(new SchemataSchedulingFeature(), services);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(RunJobHandler));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(CancelOperationHandler));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(WaitOperationHandler));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<IOptions<SchemataResourceOptions>>();
        Assert.True(options is null || !options.Value.Resources.ContainsKey(typeof(SchemataJob).TypeHandle));
        Assert.True(options is null || !options.Value.Resources.ContainsKey(typeof(SchemataJobExecution).TypeHandle));
    }

    [Fact]
    public void RegisterHttpResourcesHandlersAndMethods_WhenHttpBridgeIsInstalled() {
        var services = new ServiceCollection();

        Configure(new SchemataSchedulingHttpFeature(), services);

        Assert.Contains(services, d => d.ServiceType == typeof(RunJobHandler));
        AssertHandlersRegistered(services);
        AssertSchedulingResources(BuildResourceOptions(services), HttpResourceAttribute.Name);
    }

    [Fact]
    public void RegisterGrpcResourcesHandlersAndMethods_WhenGrpcBridgeIsInstalled() {
        var services = new ServiceCollection();

        Configure(new SchemataSchedulingGrpcFeature(), services);

        Assert.Contains(services, d => d.ServiceType == typeof(RunJobHandler));
        AssertHandlersRegistered(services);
        AssertSchedulingResources(BuildResourceOptions(services), GrpcResourceAttribute.Name);
    }

    [Theory]
    [InlineData(typeof(SchemataSchedulingHttpFeature))]
    [InlineData(typeof(SchemataSchedulingGrpcFeature))]
    public async Task RegisterDispatcherAndExecuteWork_WhenBridgeIsInstalled(Type featureType) {
        var services = new ServiceCollection();
        var ran      = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new ImmediateScheduler();

        Configure((FeatureBase)Activator.CreateInstance(featureType)!, services);
        services.Replace(ServiceDescriptor.Singleton<IScheduler>(scheduler));
        services.AddSingleton(new OperationDescriptor("test:op", "test", typeof(BridgeArgs)));
        services.AddKeyedSingleton<IOperationHandler<BridgeArgs>>("test:op", new BridgeHandler(ran));

        using var provider = services.BuildServiceProvider();
        scheduler.Services = provider;

        var dispatcher = provider.GetRequiredService<IOperationDispatcher>();

        var operation = await dispatcher.DispatchAsync("test:op", new BridgeArgs(), CancellationToken.None);

        Assert.StartsWith("operations/", operation.CanonicalName, StringComparison.Ordinal);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void AssertHandlersRegistered(IServiceCollection services) {
        Assert.Contains(services, d => d.ServiceType == typeof(RunJobHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(CancelOperationHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(WaitOperationHandler));
    }

    private static void AssertSchedulingResources(SchemataResourceOptions options, string endpoint) {
        var job = options.Resources[typeof(SchemataJob).TypeHandle];
        Assert.Equal(typeof(SchemataJob), job.Entity);
        Assert.Equal(typeof(SchemataJob), job.Request);
        Assert.Equal(typeof(SchemataJob), job.Detail);
        Assert.Equal(typeof(SchemataJob), job.Summary);
        Assert.Equal([endpoint], job.Endpoints);
        Assert.Null(job.Operations);

        var execution = options.Resources[typeof(SchemataJobExecution).TypeHandle];
        Assert.Equal(typeof(SchemataJobExecution), execution.Entity);
        Assert.Equal(typeof(Operation), execution.Request);
        Assert.Equal(typeof(Operation), execution.Detail);
        Assert.Equal(typeof(Operation), execution.Summary);
        Assert.Equal([endpoint], execution.Endpoints);
        Assert.Equal([Operations.Get, Operations.List, Operations.Delete], execution.Operations);

        var jobMethod = Assert.Single(options.Methods[typeof(SchemataJob).TypeHandle]);
        Assert.Equal("run", jobMethod.Verb);
        Assert.Equal(typeof(RunJobHandler), jobMethod.Handler);

        var executionMethods = options.Methods[typeof(SchemataJobExecution).TypeHandle]
                                      .Where(m => m.Verb is "cancel" or "wait")
                                      .OrderBy(m => m.Verb)
                                      .ToArray();
        Assert.Equal(2, executionMethods.Length);
        Assert.Equal("cancel", executionMethods[0].Verb);
        Assert.Equal(typeof(CancelOperationHandler), executionMethods[0].Handler);
        Assert.Equal("wait", executionMethods[1].Verb);
        Assert.Equal(typeof(WaitOperationHandler), executionMethods[1].Handler);
    }

    private static SchemataResourceOptions BuildResourceOptions(IServiceCollection services) {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;
    }

    private static void Configure(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(
            services,
            new(),
            new(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IWebHostEnvironment>());
    }

    private sealed class ImmediateScheduler : IScheduler
    {
        public IServiceProvider Services { get; set; } = null!;

        public Task StartAsync(CancellationToken ct) { return Task.CompletedTask; }

        public Task StopAsync(CancellationToken ct) { return Task.CompletedTask; }

        public Task ScheduleAsync(SchemataJob job, CancellationToken ct) { return Task.CompletedTask; }

        public Task ScheduleAsync(SchemataJob job, IReadOnlyDictionary<string, object?>? variables, CancellationToken ct) {
            return Task.CompletedTask;
        }

        public Task UnscheduleAsync(string job, CancellationToken ct) { return Task.CompletedTask; }

        public async Task<SchemataJobExecution> TriggerAsync<TJob>(JobContext context, CancellationToken ct)
            where TJob : class, IScheduledJob {
            context.ExecutionUid ??= Identifiers.NewUid();
            context.Execution = new() {
                Uid               = context.ExecutionUid.Value,
                Name              = context.ExecutionUid.Value.ToString("n"),
                CanonicalName     = $"operations/{context.ExecutionUid.Value:n}",
                Method            = context.Method,
            JobKey            = context.JobKey,
            ArgsJson          = context.ArgsJson,
            };

            var job = Services.GetRequiredService<TJob>();
            await job.ExecuteAsync(context, ct);

            return context.Execution;
        }

        public Task RescheduleAsync(SchemataJob job, JobContext? preparedContext, CancellationToken ct) {
            return Task.CompletedTask;
        }
    }

    private sealed class BridgeArgs;

    private sealed class BridgeHandler(TaskCompletionSource ran) : IOperationHandler<BridgeArgs>
    {
        public Task<object?> RunAsync(BridgeArgs args, CancellationToken ct) {
            ran.SetResult();
            return Task.FromResult<object?>(null);
        }
    }
}
