using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class EfCoreFlowFixture : IAsyncLifetime, IFlowIntegrationFixture
{
    private readonly string _dbPath = $"{Guid.NewGuid():n}.db";

    private ServiceProvider? _root;

    public SchemataFlowOptions FlowOptions { get; } = new();

    /// <summary>
    ///     The catch kinds the fixture's registered <see cref="IFlowCatchHandler" /> answers for. A test
    ///     mutates this to simulate activating or omitting a bridge, which DI registration alone cannot
    ///     express once the container is built.
    /// </summary>
    public HashSet<FlowCatchKind> CatchKinds { get; } = [];

    /// <summary>
    ///     Canonical names whose transition the fixture's <see cref="IFlowCatchHandler" /> fails.
    ///     Arming runs inside the transition's unit of work, so throwing there rolls the delivery
    ///     back — which is how a test makes one broadcast target fail while its siblings commit.
    /// </summary>
    public HashSet<string> FailingProcesses { get; } = [];

    /// <summary>
    ///     Time each transition is held inside its unit of work. Left at zero, the advisor is inert;
    ///     a test raises it so overlapping deliveries are observable through <see cref="PeakConcurrency" />.
    /// </summary>
    public TimeSpan TransitionDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Highest number of transitions the fixture saw running at the same time.</summary>
    public int PeakConcurrency => Volatile.Read(ref _peakConcurrency);

    /// <summary>How many transitions the fixture armed, so a test can tell a low peak apart from no traffic.</summary>
    public int TransitionCount => Volatile.Read(ref _transitionCount);

    private int _activeTransitions;
    private int _peakConcurrency;
    private int _transitionCount;

    /// <summary>Clears the counters so one test's measurements cannot satisfy another's assertion.</summary>
    public void ResetCounters() {
        Volatile.Write(ref _peakConcurrency, 0);
        Volatile.Write(ref _transitionCount, 0);
    }

    #region IAsyncLifetime Members

    public async Task InitializeAsync() {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite($"Data Source={_dbPath}")
                                                               .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<Order, EfCoreRepository<TestDbContext, Order>>();
        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        FlowFixtureServices.AddResourceTypeResolver(
            services, typeof(Order), typeof(SchemataProcess), typeof(SchemataProcessToken));
        FlowFixtureServices.AddFlowServices(services);
        services.AddSingleton<IOptions<SchemataFlowOptions>>(Options.Create(FlowOptions));

        var catches = new Mock<IFlowCatchHandler>();
        catches.Setup(handler => handler.Handles(It.IsAny<FlowCatchKind>()))
               .Returns<FlowCatchKind>(CatchKinds.Contains);
        catches.Setup(handler => handler.ArmAsync(
                                     It.IsAny<FlowTransitionContext>(),
                                     It.IsAny<CancellationToken>()))
               .Returns(async ValueTask (FlowTransitionContext context, CancellationToken _) => {
                   Interlocked.Increment(ref _transitionCount);
                   RecordPeak(Interlocked.Increment(ref _activeTransitions));
                   try {
                       if (TransitionDelay > TimeSpan.Zero) {
                           await Task.Delay(TransitionDelay);
                       }

                       var name = context.Snapshot.Process.CanonicalName;
                       if (name is not null && FailingProcesses.Contains(name)) {
                           throw new InvalidOperationException($"Injected transition failure for '{name}'.");
                       }
                   } finally {
                       Interlocked.Decrement(ref _activeTransitions);
                   }
               });
        services.AddSingleton(catches.Object);

        _root = services.BuildServiceProvider();

        using (var scope = _root.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await FlowFixtureServices.RegisterProcessesAsync(_root);
    }

    public async Task DisposeAsync() {
        if (_root is not null) {
            using var scope = _root.CreateScope();
            var       db    = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureDeletedAsync();
            await _root.DisposeAsync();
        }

        if (File.Exists(_dbPath)) {
            File.Delete(_dbPath);
        }
    }

    #endregion

    public IServiceScope CreateScope() { return _root!.CreateScope(); }

    private void RecordPeak(int observed) {
        int current;
        while (observed > (current = Volatile.Read(ref _peakConcurrency))) {
            if (Interlocked.CompareExchange(ref _peakConcurrency, observed, current) == current) {
                return;
            }
        }
    }
}
