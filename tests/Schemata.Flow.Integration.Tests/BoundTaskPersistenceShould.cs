using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Expressions.Cel;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

using Schemata.Messaging.Skeleton;
namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class BoundTaskPersistenceShould
{
    [Theory]
    [Trait("Layer", "Component")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_Signal_Updates_One_Source_From_Two_Persisted_Tokens(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var source = await SeedAsync(fixture);
            var process = await StartAsync<TwoSignalProcess>(fixture, source);
            var waiting = await CaptureAsync(fixture, process, source.Uid);
            Assert.Equal(2, waiting.Tokens.Count(row => row.StateName.StartsWith("catch-", StringComparison.Ordinal)));
            using (var scope = fixture.CreateScope()) {
                var result = await scope.ServiceProvider.GetRequiredService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>()
                    .HandleAsync(new(process.CanonicalName!, "shared", null, null, null), CancellationToken.None);
                Assert.Equal(SignalDeliveryStatus.Delivered, result.Status);
            }
            var durable = await CaptureAsync(fixture, process, source.Uid);
            Assert.Equal("before|written|written", durable.Source.TaskValue);
            Assert.All(durable.Bindings, binding => Assert.Equal(durable.Source.Timestamp, binding.SourceTimestamp));
            Assert.Equal(2, durable.Tokens.Count(row => row.StateName.StartsWith("review-", StringComparison.Ordinal)));
            Assert.Equal(2, durable.Transitions.Count(row => row.Previous is "catch-a" or "catch-b"
                && row.Posterior is "review-a" or "review-b"));
        } finally { await lifetime.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Component")]
    public async Task Same_Scoped_Public_Runner_Commits_Sequential_Start_And_Complete(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            SchemataProcess process;
            using (var scope = fixture.CreateScope()) {
                using var ambient = AdviceContext.Establish(new AdviceContext(scope.ServiceProvider));
                await scope.ServiceProvider.GetRequiredService<IProcessRegistry>()
                    .RegisterAsync<SequentialBoundProcess>(FlowConstants.Engines.Bpmn);
                var runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
                process = await runner.StartAsync(nameof(SequentialBoundProcess), order);
                Assert.Equal("Running", process.State);
                var completed = await runner.CompleteAsync(process, null, null, CancellationToken.None);
                Assert.Equal("Completed", completed.Process.State);
            }

            var persisted = await CaptureAsync(fixture, process, order.Uid);
            Assert.Equal("42", persisted.Source.TaskValue);
            Assert.Equal("high", persisted.Source.State);
            Assert.Equal("Completed", persisted.Process.State);
            var token = Assert.Single(persisted.Tokens);
            Assert.Equal("Completed", token.State);
            Assert.Equal("end", token.StateName);
            Assert.Equal("42", token.Annotations["score"]);
            Assert.Equal("high", token.Annotations["decision"]);
            Assert.Contains(persisted.Transitions, row => row.Posterior == "review");
            Assert.Contains(persisted.Transitions, row => row.Previous == "review" && row.Posterior == "end" && row.Token == token.CanonicalName);
            var processBinding = Assert.Single(persisted.Bindings, row => row.Name == "order");
            Assert.Null(processBinding.Token);
            var tokenBinding = Assert.Single(persisted.Bindings, row => row.Name == "computed");
            Assert.Equal(token.CanonicalName, tokenBinding.Token);
            Assert.All(persisted.Bindings, row => {
                Assert.Equal(persisted.Source.CanonicalName, row.Source);
                Assert.Equal(persisted.Source.Timestamp, row.SourceTimestamp);
            });
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Component")]
    public async Task Stale_Supplied_Start_Source_Rejects_Projection_And_Rolls_Back_All_Flow_Rows(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var stale = await SeedAsync(fixture);
            using (var external = fixture.CreateScope()) {
                var updated = (await external.ServiceProvider.GetRequiredService<IRepository<Order>>().FindAsync([stale.Uid]))!;
                updated.State = "external";
                await external.ServiceProvider.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(updated);
            }
            var committed = Assert.Single(await Rows<Order>(fixture), row => row.Uid == stale.Uid);
            Assert.NotEqual(stale.Timestamp, committed.Timestamp);
            using var scope = fixture.CreateScope();
            using var ambient = AdviceContext.Establish(new AdviceContext(scope.ServiceProvider));
            var runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
            var error = await Assert.ThrowsAsync<FailedPreconditionException>(() =>
                runner.StartAsync(nameof(ProjectionProcess), stale).AsTask());
            var info = Assert.Single(error.Details!.OfType<ErrorInfoDetail>());
            Assert.Equal("FLOW_SOURCE_MODIFIED_CONCURRENTLY", info.Reason);
            Assert.Equal(stale.CanonicalName, info.Metadata!["name"]);
            var persisted = Assert.Single(await Rows<Order>(fixture), row => row.Uid == stale.Uid);
            Assert.Equal("external", persisted.State);
            Assert.Equal("before", persisted.TaskValue);
            Assert.Equal(committed.Timestamp, persisted.Timestamp);
            Assert.Empty(await Rows<SchemataProcess>(fixture));
            Assert.Empty(await Rows<SchemataProcessToken>(fixture));
            Assert.Empty(await Rows<SchemataProcessSource>(fixture));
            Assert.Empty(await Rows<SchemataProcessTransition>(fixture));
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Component")]
    public async Task Stale_Source_Binding_Rejects_Before_Task_Writes_And_Preserves_Committed_Rows(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            using var scope = fixture.CreateScope();
            using var ambient = AdviceContext.Establish(new AdviceContext(scope.ServiceProvider));
            await scope.ServiceProvider.GetRequiredService<IProcessRegistry>()
                .RegisterAsync<SequentialBoundProcess>(FlowConstants.Engines.Bpmn);
            var runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
            var process = await runner.StartAsync(nameof(SequentialBoundProcess), order);
            using (var external = fixture.CreateScope()) {
                var mutation = external.ServiceProvider.GetRequiredService<IResourceMutation<Order>>();
                var updated = (await external.ServiceProvider.GetRequiredService<IRepository<Order>>().FindAsync([order.Uid]))!;
                updated.State = "external";
                await mutation.UpdateAsync(updated);
            }
            var before = await CaptureAsync(fixture, process, order.Uid);
            var error = await Assert.ThrowsAsync<FailedPreconditionException>(() =>
                runner.CompleteAsync(process, null, null, CancellationToken.None).AsTask());
            var info = Assert.Single(error.Details!.OfType<ErrorInfoDetail>());
            Assert.Equal("FLOW_SOURCE_MODIFIED_CONCURRENTLY", info.Reason);
            Assert.Equal(before.Source.CanonicalName, info.Metadata!["name"]);
            var after = await CaptureAsync(fixture, process, order.Uid);
            Assert.Equal("before", after.Source.TaskValue);
            Assert.Equal("external", after.Source.State);
            Assert.Equal(before.Source.Timestamp, after.Source.Timestamp);
            Assert.Equal(before.Process.Timestamp, after.Process.Timestamp);
            Assert.Equal(before.Process.State, after.Process.State);
            Assert.Equal(before.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)),
                after.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)));
            Assert.Equal(before.Bindings.Select(row => (row.Uid, row.Name, row.Token, row.SourceTimestamp)),
                after.Bindings.Select(row => (row.Uid, row.Name, row.Token, row.SourceTimestamp)));
            Assert.Equal(before.Transitions.Select(row => (row.Uid, row.Previous, row.Posterior, row.Timestamp)),
                after.Transitions.Select(row => (row.Uid, row.Previous, row.Posterior, row.Timestamp)));
            Assert.All(after.Tokens, row => Assert.False(row.Annotations.ContainsKey("score")));
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Layer", "Component")]
    public async Task Called_Process_Source_Writes_Share_Initial_Stamp_With_Parent_Flush(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            using var scope = fixture.CreateScope();
            using var ambient = AdviceContext.Establish(new AdviceContext(scope.ServiceProvider));
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync<SourceCallParentProcess>(FlowConstants.Engines.Bpmn);
            await registry.RegisterAsync<SourceCallChildProcess>(FlowConstants.Engines.Bpmn);
            var runner = scope.ServiceProvider.GetRequiredService<IFlowRunner>();
            var process = await runner.StartAsync(nameof(SourceCallParentProcess), order);
            var transfer = scope.ServiceProvider.GetRequiredService<SourceTransfer>();
            transfer.Source = (await scope.ServiceProvider.GetRequiredService<IRepository<Order>>().FindAsync([order.Uid]))!;
            var snapshot = await runner.CompleteAsync(process, null, null, CancellationToken.None);
            Assert.Equal("Waiting", snapshot.Process.State);
            Assert.Equal("call", Assert.Single(snapshot.Tokens).StateName);
            var persisted = await CaptureAsync(fixture, process, order.Uid);
            Assert.Equal("42", persisted.Source.TaskValue);
            Assert.Equal("high", persisted.Source.State);
            Assert.Equal(persisted.Source.Timestamp, Assert.Single(persisted.Bindings).SourceTimestamp);
            var child = Assert.Single(await Rows<SchemataProcess>(fixture), row => row.DefinitionName == nameof(SourceCallChildProcess));
            Assert.Equal("Completed", child.State);
            var childToken = Assert.Single(await Rows<SchemataProcessToken>(fixture), row => row.Process == child.Name);
            Assert.Equal("Completed", childToken.State);
            Assert.Equal("child-end", childToken.StateName);
            Assert.Equal("42", childToken.Annotations["score"]);
            Assert.Equal("high", childToken.Annotations["decision"]);
            var childBinding = Assert.Single(await Rows<SchemataProcessSource>(fixture), row => row.Process == child.CanonicalName);
            Assert.Equal(childToken.CanonicalName, childBinding.Token);
            Assert.Equal(persisted.Source.Timestamp, childBinding.SourceTimestamp);
            Assert.Contains(persisted.Transitions, row => row.Kind == TransitionKind.Spawn && row.Posterior == child.CanonicalName);
            var finished = await runner.CompleteAsync(process, null, null, CancellationToken.None);
            Assert.Equal("Completed", finished.Process.State);
            Assert.Equal("42", (await CaptureAsync(fixture, process, order.Uid)).Source.TaskValue);
        } finally {
            await lifetime.DisposeAsync();
        }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cel_And_Keyed_Rule_Update_Source_And_Select_Persisted_Branch(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            var process = await StartAsync<BoundProcess>(fixture, order);
            using (var scope = fixture.CreateScope()) {
                var snapshot = await scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(process, null, null, CancellationToken.None);
                Assert.Equal("approved", Assert.Single(snapshot.Tokens, token => token.State == "Active").StateName);
            }
            var persisted = Assert.Single(await Rows<Order>(fixture), row => row.Uid == order.Uid);
            Assert.Equal("42", persisted.TaskValue);
            Assert.Equal("high", persisted.State);
            var token = Assert.Single(await Rows<SchemataProcessToken>(fixture), row => row.Process == process.Name && row.State == "Active");
            Assert.Equal("approved", token.StateName);
            Assert.Equal("42", token.Annotations["score"]);
            Assert.Equal("high", token.Annotations["risk"]);
            Assert.Contains(await Rows<SchemataProcessTransition>(fixture), row => row.Process == process.Name && row.Posterior == "approved");
            var bindings = (await Rows<SchemataProcessSource>(fixture)).Where(row => row.Process == process.CanonicalName).ToList();
            Assert.Contains(bindings, row => row.Name == "computed" && row.Token == token.CanonicalName);
            Assert.All(bindings, row => Assert.Equal(persisted.Timestamp, row.SourceTimestamp));
            using var resume = fixture.CreateScope();
            var completed = await resume.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(process, token.CanonicalName, null, CancellationToken.None);
            Assert.Equal("Completed", completed.Process.State);
            Assert.Equal("42", Assert.Single(await Rows<Order>(fixture), row => row.Uid == order.Uid).TaskValue);
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_MidFailure_Rolls_Back_Source_Process_Tokens_Bindings_And_Transitions(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            var process = await StartAsync<FailingBoundProcess>(fixture, order);
            var before = await CaptureAsync(fixture, process, order.Uid);
            Assert.Single(before.Tokens);
            Assert.Single(before.Bindings);
            Assert.Contains(before.Transitions, row => row.Posterior == "review");
            using (var scope = fixture.CreateScope()) {
                var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(process, null, null, CancellationToken.None).AsTask());
                Assert.IsType<InvalidCastException>(error.InnerException);
            }
            var after = await CaptureAsync(fixture, process, order.Uid);
            Assert.Equal(before.Source.Timestamp, after.Source.Timestamp);
            Assert.Equal("before", after.Source.TaskValue);
            Assert.Equal(before.Process.Timestamp, after.Process.Timestamp);
            Assert.Equal(before.Process.State, after.Process.State);
            Assert.Equal(before.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)), after.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)));
            Assert.Equal(before.Bindings.Select(row => (row.Uid, row.Name, row.SourceTimestamp)), after.Bindings.Select(row => (row.Uid, row.Name, row.SourceTimestamp)));
            Assert.Equal(before.Transitions.Select(row => (row.Uid, row.Posterior, row.Timestamp)), after.Transitions.Select(row => (row.Uid, row.Posterior, row.Timestamp)));
            Assert.All(after.Tokens, row => Assert.False(row.Annotations.ContainsKey("score")));
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cel_Error_Value_Rolls_Back_Previously_Staged_Source_And_Token_Work(bool linq, bool divide) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            var process = divide
                ? await StartAsync<DivideErrorBoundProcess>(fixture, order)
                : await StartAsync<MissingErrorBoundProcess>(fixture, order);
            var before = await CaptureAsync(fixture, process, order.Uid);
            Assert.Single(before.Tokens);
            Assert.Single(before.Bindings);
            Assert.Contains(before.Transitions, row => row.Posterior == "review");
            using (var scope = fixture.CreateScope()) {
                var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => scope.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(process, null, null, CancellationToken.None).AsTask());
                Assert.NotNull(error.Details);
                var info = Assert.Single(error.Details.OfType<ErrorInfoDetail>());
                Assert.Equal("FLOW_TASK_EXPRESSION_INVALID", info.Reason);
                Assert.Equal("dynamic", info.Metadata!["name"]);
                Assert.Equal("CEL_EVALUATION_ERROR", info.Metadata["expression_reason"]);
                Assert.Equal(divide ? "divide by zero" : "undeclared reference to 'missing' (in container '')", info.Metadata["expression_message"]);
                Assert.Equal("dynamic", Assert.Single(Assert.Single(error.Details.OfType<PreconditionFailureDetail>()).Violations!).Subject);
                var observed = scope.ServiceProvider.GetRequiredService<RuntimeErrorObservation>();
                Assert.Equal("42", observed.SourceValue);
                Assert.Equal("42", observed.TokenScore);
                Assert.Equal(MutationResult.Applied, observed.Mutation);
                Assert.False(observed.Output);
                Assert.False(observed.Guard);
            }
            var after = await CaptureAsync(fixture, process, order.Uid);
            Assert.Equal("before", after.Source.TaskValue);
            Assert.Equal(before.Source.State, after.Source.State);
            Assert.Equal(before.Source.Timestamp, after.Source.Timestamp);
            Assert.Equal(before.Process.Timestamp, after.Process.Timestamp);
            Assert.Equal(before.Process.State, after.Process.State);
            Assert.Equal(before.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)), after.Tokens.Select(row => (row.Uid, row.State, row.StateName, row.Timestamp)));
            Assert.Equal(before.Bindings.Select(row => (row.Uid, row.Name, row.Token, row.SourceTimestamp)), after.Bindings.Select(row => (row.Uid, row.Name, row.Token, row.SourceTimestamp)));
            Assert.Equal(before.Transitions.Select(row => (row.Uid, row.Posterior, row.Timestamp)), after.Transitions.Select(row => (row.Uid, row.Posterior, row.Timestamp)));
            Assert.All(after.Tokens, row => {
                Assert.False(row.Annotations.ContainsKey("score"));
                Assert.False(row.Annotations.ContainsKey("result"));
            });
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Spawned_Child_Owns_Output_And_Guard_Without_Inheriting_Parent_Annotations(bool linq, bool boundary) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var source = boundary ? await SeedAsync(fixture) : null;
            using var scope = fixture.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync(new() { Name = boundary ? nameof(BoundaryChildProcess) : nameof(RootForkProcess), DefinitionType = boundary ? typeof(BoundaryChildProcess) : typeof(RootForkProcess), Engine = FlowConstants.Engines.Bpmn });
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            var process = await runner.StartAsync(boundary ? nameof(BoundaryChildProcess) : nameof(RootForkProcess));
            if (boundary) {
                var parent = Assert.Single(await Rows<SchemataProcessToken>(fixture), row => row.Process == process.Name);
                using (var write = fixture.CreateScope()) {
                    var repository = write.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
                    var loaded = (await repository.FindAsync([parent.Uid]))!;
                    loaded.Annotations["parent_only"] = "private";
                    await repository.UpdateAsync(loaded);
                    await repository.CommitAsync();
                }
                using (var bind = fixture.CreateScope()) {
                    var bindings = bind.ServiceProvider.GetRequiredService<IRepository<SchemataProcessSource>>();
                    await bindings.AddAsync(new() { Process = process.CanonicalName!, Token = parent.CanonicalName, Name = "order", Source = source!.CanonicalName!, SourceType = typeof(Order).FullName!, SourceTimestamp = source.Timestamp });
                    await bindings.CommitAsync();
                }
                using var trigger = fixture.CreateScope();
                var delivery = await trigger.ServiceProvider.GetRequiredService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>()
                    .HandleAsync(new(process.CanonicalName!, "spawn", null, parent.CanonicalName, null), CancellationToken.None);
                Assert.Equal(SignalDeliveryStatus.Delivered, delivery.Status);
            }
            var tokens = (await Rows<SchemataProcessToken>(fixture)).Where(row => row.Process == process.Name).ToList();
            var child = Assert.Single(tokens, row => row.StateName == "approved" && row.State == "Active");
            Assert.Equal("42", child.Annotations["score"]);
            Assert.Equal(child.CanonicalName, child.Annotations["output_owner"]);
            Assert.False(child.Annotations.ContainsKey("parent_only"));
            Assert.All(tokens.Where(row => row.Uid != child.Uid), row => Assert.False(row.Annotations.ContainsKey("score")));
            if (boundary) {
                Assert.NotNull(child.Spawner);
                Assert.Equal("42", Assert.Single(await Rows<Order>(fixture), row => row.Uid == source!.Uid).TaskValue);
                Assert.DoesNotContain(await Rows<SchemataProcessSource>(fixture), row => row.Process == process.CanonicalName && row.Token == child.CanonicalName);
            }
            Assert.Contains(await Rows<SchemataProcessTransition>(fixture), row => row.Process == process.Name && row.Token == child.CanonicalName && row.Posterior == "approved");
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failing_Spawned_Child_Rolls_Back_Staging_And_Parent_Source(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var source = await SeedAsync(fixture);
            using var scope = fixture.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<FailingBoundaryChildProcess>(FlowConstants.Engines.Bpmn);
            var process = await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync(nameof(FailingBoundaryChildProcess));
            var parent = Assert.Single(await Rows<SchemataProcessToken>(fixture), row => row.Process == process.Name);
            using (var bind = fixture.CreateScope()) {
                var repository = bind.ServiceProvider.GetRequiredService<IRepository<SchemataProcessSource>>();
                await repository.AddAsync(new() { Process = process.CanonicalName!, Token = parent.CanonicalName, Name = "order", Source = source.CanonicalName!, SourceType = typeof(Order).FullName!, SourceTimestamp = source.Timestamp });
                await repository.CommitAsync();
            }
            var before = await CaptureAsync(fixture, process, source.Uid);
            Assert.Single(before.Tokens);
            using (var trigger = fixture.CreateScope()) {
                var failure = await trigger.ServiceProvider.GetRequiredService<IRequestHandler<DeliverSignalRequest, SignalDeliveryResult>>()
                    .HandleAsync(new(process.CanonicalName!, "spawn", null, parent.CanonicalName, null), CancellationToken.None);
                Assert.Equal(SignalDeliveryStatus.Failed, failure.Status);
                Assert.IsType<FailedPreconditionException>(failure.Error);
            }
            var after = await CaptureAsync(fixture, process, source.Uid);
            Assert.Equal("before", after.Source.TaskValue);
            Assert.Equal(before.Source.Timestamp, after.Source.Timestamp);
            Assert.Equal(before.Process.Timestamp, after.Process.Timestamp);
            Assert.Equal(before.Tokens.Select(row => (row.Uid, row.State, row.Timestamp)), after.Tokens.Select(row => (row.Uid, row.State, row.Timestamp)));
            Assert.Equal(before.Bindings.Select(row => (row.Uid, row.Token, row.SourceTimestamp)), after.Bindings.Select(row => (row.Uid, row.Token, row.SourceTimestamp)));
            Assert.Equal(before.Transitions.Select(row => row.Uid), after.Transitions.Select(row => row.Uid));
        } finally { await lifetime.DisposeAsync(); }
    }

    [Trait("Layer", "Component")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_Typed_Rule_Uses_Original_Version_And_Cannot_Be_Rebound(bool linq) {
        var fixture = CreateFixture(linq);
        var lifetime = (IAsyncLifetime)fixture;
        await lifetime.InitializeAsync();
        try {
            var order = await SeedAsync(fixture);
            using var scope = fixture.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
            await registry.RegisterAsync<BoundProcess>(FlowConstants.Engines.Bpmn, c => { c.Name = "history"; c.Version = "one"; });
            var old = await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync("history", order, new StartProcessOptions { DefinitionVersion = "one" }, null, CancellationToken.None);
            await registry.RegisterAsync<ReplacementBoundProcess>(FlowConstants.Engines.Bpmn, c => { c.Name = "history"; c.Version = "two"; c.IsLatest = true; });
            await registry.UnregisterAsync("history", "two");
            await Assert.ThrowsAsync<AlreadyExistsException>(() => registry.RegisterAsync<BoundProcess>(FlowConstants.Engines.Bpmn, c => { c.Name = "history"; c.Version = "two"; }).AsTask());
            using var resumed = fixture.CreateScope();
            await resumed.ServiceProvider.GetRequiredService<FlowRunner>().CompleteAsync(old, null, null, CancellationToken.None);
            var persisted = Assert.Single(await Rows<Order>(fixture), row => row.Uid == order.Uid);
            Assert.Equal("42", persisted.TaskValue);
            Assert.Equal("high", persisted.State);
            Assert.Equal("one", Assert.Single(await Rows<SchemataProcess>(fixture), row => row.Uid == old.Uid).DefinitionVersion);
        } finally { await lifetime.DisposeAsync(); }
    }

    private static IFlowIntegrationFixture CreateFixture(bool linq) {
        Action<IServiceCollection> configure = services => {
            services.AddCelExpressions();
            services.AddSingleton(new RuntimeErrorObservation());
            services.AddScoped<SourceTransfer>();
            var boundary = new Mock<IRiskService>();
            boundary.Setup(value => value.Classify(42L)).Returns("high");
            boundary.Setup(value => value.Classify(63L)).Returns("low");
            services.AddSingleton(boundary.Object);
            services.AddKeyedSingleton<IFlowRuleHandler<long, string>, RiskHandler>(("risk", "one"));
            services.AddKeyedSingleton<IFlowRuleHandler<long, string>, ReplacementRiskHandler>(("risk", "two"));
            services.AddKeyedSingleton<IFlowRuleHandler<long, string>, SequentialDecisionHandler>(("sequential", "1"));
        };
        if (linq) {
            var fixture = new LinqToDbFlowFixture { ConfigureServices = configure };
            fixture.CatchKinds.Add(FlowCatchKind.Signal);
            return fixture;
        }
        var ef = new EfCoreFlowFixture { ConfigureServices = configure };
        ef.CatchKinds.Add(FlowCatchKind.Signal);
        return ef;
    }

    private static async Task<Order> SeedAsync(IFlowIntegrationFixture fixture) {
        using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order { Uid = Guid.NewGuid(), Name = Guid.NewGuid().ToString("n"), CanonicalName = $"orders/{Guid.NewGuid():n}", Timestamp = Guid.NewGuid(), State = "new", TaskValue = "before" };
        await repository.AddAsync(order);
        await repository.CommitAsync();
        return order;
    }

    private static async Task<SchemataProcess> StartAsync<T>(IFlowIntegrationFixture fixture, Order order) where T : ProcessDefinition {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IProcessRegistry>().RegisterAsync<T>(FlowConstants.Engines.Bpmn);
        return await scope.ServiceProvider.GetRequiredService<FlowRunner>().StartAsync(typeof(T).Name, order, null, null, CancellationToken.None);
    }

    private static async Task<List<T>> Rows<T>(IFlowIntegrationFixture fixture) where T : class {
        using var scope = fixture.CreateScope();
        var result = new List<T>();
        await foreach (var row in scope.ServiceProvider.GetRequiredService<IRepository<T>>().ListAsync<T>(null!)) result.Add(row);
        return result;
    }

    private static async Task<Persisted> CaptureAsync(IFlowIntegrationFixture fixture, SchemataProcess process, Guid order) => new(
        Assert.Single(await Rows<Order>(fixture), row => row.Uid == order),
        Assert.Single(await Rows<SchemataProcess>(fixture), row => row.Uid == process.Uid),
        (await Rows<SchemataProcessToken>(fixture)).Where(row => row.Process == process.Name).OrderBy(row => row.Name).ToList(),
        (await Rows<SchemataProcessSource>(fixture)).Where(row => row.Process == process.CanonicalName).OrderBy(row => row.Name).ToList(),
        (await Rows<SchemataProcessTransition>(fixture)).Where(row => row.Process == process.Name).OrderBy(row => row.Name).ToList());

    private sealed record Persisted(Order Source, SchemataProcess Process, List<SchemataProcessToken> Tokens, List<SchemataProcessSource> Bindings, List<SchemataProcessTransition> Transitions);
    public interface IRiskService { string Classify(long score); }
    public sealed class RiskHandler(IRiskService service) : IFlowRuleHandler<long, string>
    {
        public ValueTask<BusinessRuleResult<string>> EvaluateAsync(long input, FlowTaskContext context, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new BusinessRuleResult<string>(true, service.Classify(input)));
        }
    }
    public sealed class ReplacementRiskHandler(IRiskService service) : IFlowRuleHandler<long, string>
    {
        public ValueTask<BusinessRuleResult<string>> EvaluateAsync(long input, FlowTaskContext context, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new BusinessRuleResult<string>(true, service.Classify(input) + "-v2"));
        }
    }

    public sealed class SequentialDecisionHandler : IFlowRuleHandler<long, string>
    {
        public ValueTask<BusinessRuleResult<string>> EvaluateAsync(long input, FlowTaskContext context, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new BusinessRuleResult<string>(true, input >= 40 ? "high" : "low"));
        }
    }

    public sealed class TwoSignalProcess : ProcessDefinition
    {
        public TwoSignalProcess() {
            BindSource<Order>(projection: FlowSourceProjection.None);
            var start = new StartEvent { Name = "start" };
            var fork = new ParallelGateway { Name = "fork" };
            var signal = new Signal { Name = "shared" };
            Elements.AddRange([start, fork]);
            Signals.Add(signal);
            Flows.Add(new() { Source = start, Target = fork });
            foreach (var suffix in new[] { "a", "b" }) {
                var caught = new FlowEvent { Name = "catch-" + suffix, Position = EventPosition.IntermediateCatch, Definition = signal };
                var write = new ProcedureTask { Name = "write-" + suffix, Body = async (context, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.TaskValue += "|written";
                } };
                var review = new UserTask { Name = "review-" + suffix };
                var end = new EndEvent { Name = "end-" + suffix };
                Elements.AddRange([caught, write, review, end]);
                Flows.Add(new() { Source = fork, Target = caught });
                Flows.Add(new() { Source = caught, Target = write });
                Flows.Add(new() { Source = write, Target = review });
                Flows.Add(new() { Source = review, Target = end });
            }
        }
    }

    public sealed class SequentialBoundProcess : ProcessDefinition
    {
        public SequentialBoundProcess() {
            BindSource<Order>(projection: FlowSourceProjection.None);
            BindSource<Order>("computed", FlowSourceProjection.None);
            var start = new StartEvent { Name = "start" };
            var review = new UserTask { Name = "review" };
            var script = new ScriptTask<Order, long> {
                Name = "compute", Language = "cel", Script = "task_value == 'before' ? 21 * 2 : 0",
                Input = (context, ct) => context.SourceAsync<Order>(ct),
                Output = async (context, score, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.TaskValue = score.ToString(CultureInfo.InvariantCulture);
                    context.Token.Annotations["score"] = source.TaskValue;
                    await context.BindSourceAsync("computed", source, ct);
                    await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                    await context.BindSourceAsync("computed", source, ct);
                },
            };
            var rule = new BusinessRuleTask<long, string> {
                Name = "decision", Key = "sequential", Version = "1",
                Input = async (context, ct) => long.Parse((await context.SourceAsync<Order>("computed", ct)).TaskValue!, CultureInfo.InvariantCulture),
                Output = async (context, decision, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.State = decision;
                    context.Token.Annotations["decision"] = decision;
                    await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                },
            };
            var end = new EndEvent { Name = "end" };
            Elements.AddRange([start, review, script, rule, end]);
            Flows.AddRange([
                new() { Source = start, Target = review }, new() { Source = review, Target = script },
                new() { Source = script, Target = rule }, new() { Source = rule, Target = end },
            ]);
        }
    }

    public sealed class SourceTransfer
    {
        public Order Source { get; set; } = null!;
    }

    public sealed class SourceCallParentProcess : ProcessDefinition
    {
        public SourceCallParentProcess() {
            BindSource<Order>(projection: FlowSourceProjection.None);
            var start = new StartEvent { Name = "start" };
            var review = new UserTask { Name = "review" };
            var call = new CallActivity { Name = "call", CalledElement = nameof(SourceCallChildProcess) };
            var end = new EndEvent { Name = "end" };
            Elements.AddRange([start, review, call, end]);
            Flows.AddRange([new() { Source = start, Target = review }, new() { Source = review, Target = call }, new() { Source = call, Target = end }]);
        }
    }

    public sealed class SourceCallChildProcess : ProcessDefinition
    {
        public SourceCallChildProcess() {
            BindSource<Order>(projection: FlowSourceProjection.None);
            var start = new StartEvent { Name = "child-start" };
            var script = new ScriptTask<Order, long> {
                Name = "child-compute", Language = "cel", Script = "task_value == 'before' ? 21 * 2 : 0",
                Input = async (context, ct) => {
                    var source = context.GetRequiredService<SourceTransfer>().Source;
                    await context.BindSourceAsync(source, ct);
                    return await context.SourceAsync<Order>(ct);
                },
                Output = async (context, score, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.TaskValue = score.ToString(CultureInfo.InvariantCulture);
                    context.Token.Annotations["score"] = source.TaskValue;
                    await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                },
            };
            var rule = new BusinessRuleTask<long, string> {
                Name = "child-decision", Key = "sequential", Version = "1",
                Input = async (context, ct) => long.Parse((await context.SourceAsync<Order>(ct)).TaskValue!, CultureInfo.InvariantCulture),
                Output = async (context, decision, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.State = decision;
                    context.Token.Annotations["decision"] = decision;
                    await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                },
            };
            var end = new EndEvent { Name = "child-end" };
            Elements.AddRange([start, script, rule, end]);
            Flows.AddRange([new() { Source = start, Target = script }, new() { Source = script, Target = rule }, new() { Source = rule, Target = end }]);
        }
    }

    public class BoundProcess : ProcessDefinition
    {
        public BoundProcess() : this(false, false) { }
        protected BoundProcess(bool fail, bool replacement) {
            BindSource<Order>(projection: FlowSourceProjection.None);
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var review = new UserTask { Name = "review" };
            var script = new ScriptTask<Order, long> {
                Name = "compute", Language = "cel", Script = replacement ? "task_value == 'before' ? 21 * 3 : 0" : "task_value == 'before' ? 21 * 2 : 0",
                Input = (context, ct) => context.SourceAsync<Order>(ct),
                Output = async (context, score, ct) => {
                    var source = await context.SourceAsync<Order>(ct);
                    source.TaskValue = score.ToString(CultureInfo.InvariantCulture);
                    context.Token.Annotations["score"] = source.TaskValue;
                    await context.BindSourceAsync("computed", source, ct);
                    if (fail) {
                        await context.BindSourceAsync("temporary", source, ct);
                        await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                        throw new InvalidCastException("Task output mapping failed after staging.");
                    }
                },
            };
            var rule = new BusinessRuleTask<long, string> {
                Name = "risk", Key = "risk", Version = replacement ? "two" : "one",
                Input = async (context, ct) => long.Parse((await context.SourceAsync<Order>("computed", ct)).TaskValue!, CultureInfo.InvariantCulture),
                Output = async (context, risk, ct) => { (await context.SourceAsync<Order>(ct)).State = risk; context.Token.Annotations["risk"] = risk; },
            };
            var decide = new ExclusiveGateway { Name = "decide" };
            var approved = new UserTask { Name = "approved" };
            var rejected = new UserTask { Name = "rejected" };
            var end = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.AddRange([start, review, script, rule, decide, approved, rejected, end]);
            Flows.AddRange([
                new() { Source = start, Target = review }, new() { Source = review, Target = script }, new() { Source = script, Target = rule }, new() { Source = rule, Target = decide },
                new() { Source = decide, Target = approved, Condition = new SourceConditionExpression<Order>("order", source => source.TaskValue == "42" && source.State == "high") },
                new() { Source = decide, Target = rejected, IsDefault = true }, new() { Source = approved, Target = end }, new() { Source = rejected, Target = end },
            ]);
        }
    }
    public sealed class FailingBoundProcess : BoundProcess { public FailingBoundProcess() : base(true, false) { } }
    public sealed class ReplacementBoundProcess : BoundProcess { public ReplacementBoundProcess() : base(false, true) { } }

    private sealed class RuntimeErrorObservation
    {
        public string? SourceValue { get; set; }
        public string? TokenScore { get; set; }
        public MutationResult Mutation { get; set; }
        public bool Output { get; set; }
        public bool Guard { get; set; }
    }

    public abstract class RuntimeErrorBoundProcess : BoundProcess
    {
        protected RuntimeErrorBoundProcess(string expression) {
            var compute = Elements.OfType<ScriptTask<Order, long>>().Single();
            var apply = compute.Output!;
            compute.Output = async (context, value, ct) => {
                await apply(context, value, ct);
                var source = await context.SourceAsync<Order>(ct);
                context.GetRequiredService<RuntimeErrorObservation>().Mutation = await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
            };
            var rule = Elements.OfType<BusinessRuleTask<long, string>>().Single();
            var task = new ScriptTask<IReadOnlyDictionary<string, object?>, object> {
                Name = "dynamic", Language = "cel", Script = expression,
                Input = async (context, ct) => {
                    var observed = context.GetRequiredService<RuntimeErrorObservation>();
                    observed.SourceValue = (await context.SourceAsync<Order>("computed", ct)).TaskValue;
                    observed.TokenScore = context.Token.Annotations["score"];
                    return new Dictionary<string, object?> { ["numerator"] = 42L, ["denominator"] = 0L };
                },
                Output = (context, _, _) => {
                    context.GetRequiredService<RuntimeErrorObservation>().Output = true;
                    context.Token.Annotations["result"] = "applied";
                    return ValueTask.CompletedTask;
                },
            };
            Elements.Remove(rule);
            Elements.Add(task);
            foreach (var flow in Flows) {
                if (flow.Source == rule) flow.Source = task;
                if (flow.Target == rule) flow.Target = task;
            }
            Flows.Single(flow => flow.Condition is not null).Condition = new LambdaConditionExpression {
                Lambda = context => {
                    context.Execution.Services.GetRequiredService<RuntimeErrorObservation>().Guard = true;
                    return ValueTask.FromResult(true);
                },
            };
        }
    }

    public sealed class MissingErrorBoundProcess : RuntimeErrorBoundProcess
    {
        public MissingErrorBoundProcess() : base("missing") { }
    }

    public sealed class DivideErrorBoundProcess : RuntimeErrorBoundProcess
    {
        public DivideErrorBoundProcess() : base("numerator / denominator") { }
    }

    private static ScriptTask<ScoreInput, long> ChildScript() => new() {
        Name = "compute", Language = "cel", Script = "factor * 2",
        Input = (_, ct) => { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = 21 }); },
        Output = (context, score, ct) => { ct.ThrowIfCancellationRequested(); context.Token.Annotations["score"] = score.ToString(CultureInfo.InvariantCulture); context.Token.Annotations["output_owner"] = context.Token.CanonicalName; return ValueTask.CompletedTask; },
    };
    private static void AddChildTail(ProcessDefinition definition, ScriptTask<ScoreInput, long> script) {
        var decision = new ExclusiveGateway { Name = "decision" };
        var approved = new UserTask { Name = "approved" };
        var rejected = new UserTask { Name = "rejected" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        definition.Elements.AddRange([script, decision, approved, rejected, end]);
        definition.Flows.AddRange([
            new() { Source = script, Target = decision },
            new() { Source = decision, Target = approved, Condition = new LambdaConditionExpression { Lambda = context => ValueTask.FromResult(context.TokenEntity!.Annotations.TryGetValue("score", out var value) && value == "42" && context.TokenEntity.Annotations.TryGetValue("output_owner", out var owner) && owner == context.TokenEntity.CanonicalName) } },
            new() { Source = decision, Target = rejected, IsDefault = true }, new() { Source = approved, Target = end }, new() { Source = rejected, Target = end },
        ]);
    }
    public sealed class ScoreInput { public long Factor { get; set; } }
    public sealed class RootForkProcess : ProcessDefinition
    {
        public RootForkProcess() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var fork = new ParallelGateway { Name = "fork" };
            var other = new UserTask { Name = "other" };
            var script = ChildScript();
            AddChildTail(this, script);
            Elements.AddRange([start, fork, other]);
            Flows.AddRange([new() { Source = start, Target = fork }, new() { Source = fork, Target = script }, new() { Source = fork, Target = other }, new() { Source = other, Target = Elements.Single(element => element.Name == "end") }]);
        }
    }
    public class BoundaryChildProcess : ProcessDefinition
    {
        public BoundaryChildProcess() : this(false) { }
        protected BoundaryChildProcess(bool fail) {
            BindSource<Order>(projection: FlowSourceProjection.None);
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var host = new UserTask { Name = "host" };
            var boundary = new FlowEvent { Name = "spawn-boundary", Position = EventPosition.Boundary, AttachedTo = host, Interrupting = false, Definition = new Signal { Name = "spawn" } };
            Signals.Add((Signal)boundary.Definition!);
            var script = ChildScript();
            script.Input = async (context, ct) => {
                var source = await context.SourceAsync<Order>(ct);
                return new ScoreInput { Factor = source.TaskValue == "before" ? 21 : 0 };
            };
            script.Output = async (context, score, ct) => {
                var source = await context.SourceAsync<Order>(ct);
                source.TaskValue = score.ToString(CultureInfo.InvariantCulture);
                context.Token.Annotations["score"] = source.TaskValue;
                context.Token.Annotations["output_owner"] = context.Token.CanonicalName;
                if (fail) {
                    await context.GetRequiredService<IResourceMutation<Order>>().UpdateAsync(source, context.UnitOfWork, ct: ct);
                    throw new InvalidCastException("Spawned task output failed after staging.");
                }
            };
            AddChildTail(this, script);
            Elements.AddRange([start, host, boundary]);
            Flows.AddRange([new() { Source = start, Target = host }, new() { Source = host, Target = Elements.Single(element => element.Name == "end") }, new() { Source = boundary, Target = script }]);
        }
    }
    public sealed class FailingBoundaryChildProcess : BoundaryChildProcess { public FailingBoundaryChildProcess() : base(true) { } }
}
