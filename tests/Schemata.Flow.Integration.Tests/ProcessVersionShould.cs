using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public sealed class ProcessVersionShould
{
    [Fact]
    public async Task Keep_Healthy_Signal_Recipients_When_One_Historical_Version_Is_Missing() {
        var fixture = new EfCoreFlowFixture();
        await fixture.InitializeAsync();
        try {
            fixture.CatchKinds.Add(FlowCatchKind.Signal);
            using var scope = fixture.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            await registry.RegisterAsync<SignalBroadcastProcess>(FlowConstants.Engines.Bpmn, c => { c.Name = "missing-signal"; });
            await registry.RegisterAsync<SignalBroadcastProcess>(FlowConstants.Engines.Bpmn, c => { c.Name = "healthy-signal"; });
            var missing = await runner.StartAsync("missing-signal");
            var healthy = await runner.StartAsync("healthy-signal");
            await registry.UnregisterAsync("missing-signal", "1");
            var results = await runner.ThrowSignalAsync("broadcast-signal", (string?)null, null, null, CancellationToken.None);
            Assert.Equal(SignalDeliveryStatus.Delivered, Assert.Single(results, r => r.ProcessCanonicalName == healthy.CanonicalName).Status);
            var failure = Assert.Single(results, r => r.ProcessCanonicalName == missing.CanonicalName);
            Assert.Equal(SignalDeliveryStatus.Failed, failure.Status);
            Assert.IsType<FailedPreconditionException>(failure.Error);
        } finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resume_Original_Graph_After_Latest_Changes_And_Reject_Missing_History(bool linq) {
        IFlowIntegrationFixture fixture = linq ? new LinqToDbFlowFixture() : new EfCoreFlowFixture();
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            using var scope = fixture.CreateScope();
            var services = scope.ServiceProvider;
            var registry = services.GetRequiredService<IProcessRegistry>();
            var runner = services.GetRequiredService<FlowRunner>();
            await registry.RegisterAsync<Original>(configure: c => { c.Name = "versioned"; c.Version = "one"; c.IsLatest = true; });
            var old = await runner.StartAsync("versioned", new StartProcessOptions { DefinitionVersion = "latest" });
            await registry.RegisterAsync<Replacement>(configure: c => { c.Name = "versioned"; c.Version = "two"; c.IsLatest = true; });
            var current = await runner.StartAsync("versioned", new StartProcessOptions { DefinitionVersion = "latest" });
            Assert.Equal("one", old.DefinitionVersion);
            Assert.Equal("two", current.DefinitionVersion);
            await Assert.ThrowsAsync<AlreadyExistsException>(() => registry.RegisterAsync<Replacement>(configure: c => { c.Name = "versioned"; c.Version = "one"; c.IsLatest = true; }).AsTask());
            var published = registry.GetRegistration("versioned", "one")!.Definition;
            Assert.Throws<InvalidOperationException>(() => published.Flows.Clear());
            Assert.Throws<InvalidOperationException>(() => published.Flows[0].Target = new UserTask { Name = "replacement" });
            using (var resumed = fixture.CreateScope()) {
                var completed = await resumed.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(old, null, null, CancellationToken.None);
                Assert.Equal("Completed", completed.Process.State);
            }
            var processes = services.GetRequiredService<IRepository<SchemataProcess>>();
            var before = await processes.FindAsync([current.Uid]);
            Assert.NotNull(before);
            var stamp = before.Timestamp;
            await registry.UnregisterAsync("versioned", "two");
            await Assert.ThrowsAsync<AlreadyExistsException>(() => registry.RegisterAsync<Original>(configure: c => {
                c.Name = "versioned"; c.Version = "two";
            }).AsTask());
            using (var missing = fixture.CreateScope()) {
                await Assert.ThrowsAsync<FailedPreconditionException>(() => missing.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(current, null, null, CancellationToken.None).AsTask());
            }
            using (var persisted = fixture.CreateScope()) {
                var after = await persisted.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>().FindAsync([current.Uid]);
                Assert.Equal(stamp, after!.Timestamp);
                Assert.Equal(before.State, after.State);
                Assert.Equal("two", after.DefinitionVersion);
            }
            Assert.Null(registry.GetRegistration("versioned", "latest"));
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    public sealed class Original : ProcessDefinition
    {
        public Original() { this.Start().Go(Review); this.During(Review).End(); }
        public UserTask Review { get; } = null!;
    }

    public sealed class Replacement : ProcessDefinition
    {
        public Replacement() { this.Start().Go(Review); this.During(Review).Go(Second); this.During(Second).End(); }
        public UserTask Review { get; } = null!;
        public UserTask Second { get; } = null!;
    }
}
