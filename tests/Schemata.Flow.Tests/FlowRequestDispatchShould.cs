using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Core.Features;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.StateMachine.Features;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Flow.Tests;

public sealed class FlowRequestDispatchShould
{
    [Fact]
    public void ConfigureServices_Registers_Keyed_Default_And_Unkeyed_Request_Handler() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<SchemataFlowOptions>();

        Configure(new SchemataFlowFeature(), services);

        var request = typeof(FlowRunner).Assembly.GetType(
            "Schemata.Flow.Foundation.Commands.StartProcessRequest");
        Assert.NotNull(request);

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request, typeof(SchemataProcess));
        var handlers = typeof(FlowConstants).GetNestedType("Handlers");
        Assert.NotNull(handlers);
        var key = handlers.GetField("Default")?.GetRawConstantValue();
        Assert.NotNull(key);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService(handlerType));
        Assert.NotNull(provider.GetKeyedService(handlerType, key));
    }

    [Fact]
    public async Task StartAsync_With_Foundation_And_StateMachine_Starts_Without_Event_Publisher() {
        var processes     = Repository<SchemataProcess>();
        var tokens        = Repository<SchemataProcessToken>();
        var transitions   = Repository<SchemataProcessTransition>();
        var sources       = Repository<SchemataProcessSource>();
        var compensations = Repository<SchemataProcessCompensation>();
        processes.Setup(repository => repository.Begin()).Returns(Mock.Of<IUnitOfWork>());

        var services = new ServiceCollection()
                      .AddLogging()
                      .AddSingleton(processes.Object)
                      .AddSingleton(tokens.Object)
                      .AddSingleton(transitions.Object)
                      .AddSingleton(sources.Object)
                      .AddSingleton(compensations.Object);
        services.Configure<SchemataFlowOptions>(options => options.Configurations.Add(new() {
            Name           = "standalone",
            DefinitionType = typeof(StandaloneProcess),
        }));
        Configure(new SchemataFlowStateMachineFeature(), services);
        Configure(new SchemataFlowFeature(), services);

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();
        var runner  = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
        var process = await runner.StartAsync("standalone");

        Assert.Equal("standalone", process.DefinitionName);
        Assert.Null(scope.ServiceProvider.GetService<IEventBus>());
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

    private static Mock<IRepository<TEntity>> Repository<TEntity>()
        where TEntity : class {
        var data       = new List<TEntity>();
        var repository = new Mock<IRepository<TEntity>>();
        repository.Setup(current => current.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(current => current.Begin()).Returns(Mock.Of<IUnitOfWork>());
        repository.Setup(current => current.AddAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Returns((TEntity entity, CancellationToken _) => {
                      data.Add(entity);
                      return Task.CompletedTask;
                  });
        repository.Setup(current => current.UpdateAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(current => current.ListAsync<TEntity>(
                             It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken _) =>
                               AsAsyncEnumerable(query(data.AsQueryable()).ToList()));
        repository.Setup(current => current.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken _) =>
                               new(query(data.AsQueryable()).SingleOrDefault()));
        repository.Setup(current => current.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken _) =>
                               new(query(data.AsQueryable()).FirstOrDefault()));
        return repository;
    }

    private static async IAsyncEnumerable<TEntity> AsAsyncEnumerable<TEntity>(IEnumerable<TEntity> entities) {
        foreach (var entity in entities) {
            yield return entity;
        }

        await Task.CompletedTask;
    }

    private sealed class StandaloneProcess : ProcessDefinition
    {
        public StandaloneProcess() {
            this.Start().Go(Done);
            this.During(Done).End();
        }

        public UserTask Done { get; } = null!;
    }
}
