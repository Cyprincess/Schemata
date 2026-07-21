// SourceEntity is a structural type token for the source-binding repository registered by this harness.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_MultiInstanceShould
{
    [Fact]
    public async Task Execute_WithLoopCardinalityZero_SkipsActivityEntirely() {
        var harness = Harness.Create(MultiInstanceDefinition(0, false));

        var snapshot = await harness.StartAsync();

        Assert.DoesNotContain(snapshot.Transitions, t => t is { Previous: "multi", Posterior: "multi" });
        Assert.Equal("after", Assert.Single(snapshot.Tokens).StateName);
        Assert.Equal(0, ReadInt(snapshot.Tokens.Single(), "nrOfInstances"));
    }

    [Fact]
    public async Task Execute_ParallelWithCardinalityFive_SpawnsFiveSiblingsDuringFork() {
        var harness = Harness.Create(MultiInstanceDefinition(5, false));

        var snapshot = await harness.StartAsync();

        var parent   = Assert.Single(snapshot.Tokens, t => t is { State: "Waiting", StateName: "multi" });
        var siblings = harness.Tokens.Where(t => t.Spawner == parent.CanonicalName).ToList();
        Assert.Equal(5, siblings.Count);
        Assert.Equal([0, 1, 2, 3, 4], siblings.Select(t => ReadInt(t, "loopCounter")).OrderBy(v => v));
        Assert.All(siblings, t => Assert.Equal("Active", t.State));
        Assert.Equal("multi", parent.WaitingAtName);
        Assert.Single(harness.Transitions, t => t.Kind == TransitionKind.Fork && t.Token == parent.CanonicalName);
    }

    [Fact]
    public async Task Complete_ParallelAllFiveSiblings_JoinsToOneOutgoingToken() {
        var harness = Harness.Create(MultiInstanceDefinition(5, false));
        var snapshot = await harness.StartAsync();

        snapshot = await harness.CompleteSiblingsAsync(snapshot, 5);

        var parent = Assert.Single(snapshot.Tokens, t => t.Spawner is null);
        Assert.Equal("Active", parent.State);
        Assert.Equal("after", parent.StateName);
        Assert.Null(parent.WaitingAtName);
        Assert.Single(harness.Transitions, t => t.Kind == TransitionKind.Join && t.Token == parent.CanonicalName);
        Assert.Equal(5, ReadInt(parent, "nrOfCompletedInstances"));
    }

    [Fact]
    public async Task Execute_ParallelWithCompletionConditionAfterTwo_TerminatesRemaining() {
        var harness = Harness.Create(MultiInstanceDefinition(5, false, 2));
        var snapshot = await harness.StartAsync();

        snapshot = await harness.CompleteSiblingsAsync(snapshot, 2);

        var parent = Assert.Single(snapshot.Tokens, t => t.Spawner is null);
        var cancelled = harness.Tokens.Where(t => t.Spawner == parent.CanonicalName && t.State == "Cancelled").ToList();
        Assert.Equal(3, cancelled.Count);
        Assert.Equal(3, harness.Transitions.Count(t => t.Kind == TransitionKind.Cancel));
        Assert.Equal("Active", parent.State);
        Assert.Equal("after", parent.StateName);
        Assert.Equal(2, ReadInt(parent, "nrOfCompletedInstances"));
        Assert.Equal(0, ReadInt(parent, "nrOfActiveInstances"));
    }

    [Fact]
    public async Task Complete_ParallelUsesCompletedInstanceSourceBindingForCompletionCondition() {
        var definition = MultiInstanceDefinition(
            3,
            false,
            completionCondition: new SourceConditionExpression<SourceEntity>("order", source => source.Name == "stop"));
        var harness  = Harness.Create(definition);
        var snapshot = await harness.StartAsync();

        harness.BindSources(snapshot, counter => counter == 1 ? "stop" : "continue");
        snapshot = await harness.CompleteSiblingAsync(snapshot, 0);

        var parent = Assert.Single(snapshot.Tokens, t => t.Spawner is null);
        Assert.Equal("Waiting", parent.State);
        Assert.Equal(1, ReadInt(parent, "nrOfCompletedInstances"));

        snapshot = await harness.CompleteSiblingAsync(snapshot, 1);

        parent = Assert.Single(snapshot.Tokens, t => t.Spawner is null);
        Assert.Equal("Active", parent.State);
        Assert.Equal("after", parent.StateName);
        Assert.Single(harness.Tokens, t => t.Spawner == parent.CanonicalName && t.State == "Cancelled");
        Assert.Equal(2, ReadInt(parent, "nrOfCompletedInstances"));
    }

    [Fact]
    public async Task Execute_SequentialWithCardinalityThree_RunsInstancesInOrder() {
        var harness = Harness.Create(MultiInstanceDefinition(3, true));

        var snapshot = await harness.StartAsync();

        var iterations = snapshot.Transitions.Where(t => t is { Kind: TransitionKind.Move, Previous: "multi", Posterior: "multi" }).ToList();
        Assert.Equal(3, iterations.Count);
        Assert.Equal([0, 1, 2], iterations.Select(t => int.Parse(t.Note!, CultureInfo.InvariantCulture)));
        Assert.Equal(3, ReadInt(snapshot.Tokens.Single(), "nrOfCompletedInstances"));
    }

    [Fact]
    public async Task Execute_SequentialWithCompletionConditionAfterFirst_StopsImmediately() {
        var harness = Harness.Create(MultiInstanceDefinition(5, true, 1));

        var snapshot = await harness.StartAsync();

        var iterations = snapshot.Transitions.Where(t => t is { Kind: TransitionKind.Move, Previous: "multi", Posterior: "multi" }).ToList();
        Assert.Single(iterations);
        Assert.Equal(1, ReadInt(snapshot.Tokens.Single(), "nrOfCompletedInstances"));
    }

    [Fact]
    public async Task Execute_OneCompletedEventBehaviorOne_ThrowsNotSupported() {
        var definition = MultiInstanceDefinition(1, false, behavior: MIEventBehavior.One);
        var harness    = Harness.Create(definition);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => await harness.StartAsync());

        Assert.Contains("not supported", ex.Message, StringComparison.Ordinal);
    }

    private static ProcessDefinition MultiInstanceDefinition(
        int             cardinality,
        bool            sequential,
        int?            completionAfter = null,
        MIEventBehavior behavior        = MIEventBehavior.None,
        IConditionExpression? completionCondition = null
    ) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var multi    = new NoneTask { Name = "multi" };
        var after    = new NoneTask { Name = "after" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        multi.LoopCharacteristics = new MultiInstanceLoopCharacteristics {
            LoopCardinality           = new CardinalityExpression(cardinality),
            CompletionCondition       = completionCondition ?? (completionAfter is null ? null : new CompletedAtLeastExpression(completionAfter.Value)),
            IsSequential              = sequential,
            OneCompletedEventBehavior = behavior,
        };

        return new() {
            Name     = $"multi-{cardinality}-{sequential}",
            Elements = { start, multi, after, endEvent },
            Flows = {
                new() { Source = start, Target = multi },
                new() { Source = multi, Target = after },
                new() { Source = after, Target = endEvent },
            },
        };
    }

    private static int ReadInt(SchemataProcessToken token, string name) {
        return token.Bookkeeping.TryGetValue(name, out var value) ? value : 0;
    }

    private sealed class CardinalityExpression(int count) : IConditionExpression
    {
        public ValueTask<bool> Evaluate(FlowConditionContext context) {
            context.Bookkeeping["loopCardinality"] = count;
            return new(true);
        }
    }

    private sealed class CompletedAtLeastExpression(int count) : IConditionExpression
    {
        public ValueTask<bool> Evaluate(FlowConditionContext context) {
            return new(ReadInt(context.Bookkeeping, "nrOfCompletedInstances") >= count);
        }

        private static int ReadInt(IReadOnlyDictionary<string, int> bookkeeping, string name) {
            return bookkeeping.TryGetValue(name, out var value) ? value : 0;
        }
    }

    private sealed class Harness
    {
        private Harness(ProcessDefinition definition) {
            Definition  = definition;
            Tokens         = [];
            Transitions    = [];
            Sources        = [];
            SourceEntities = [];

            var services = new ServiceCollection();
            services.AddSingleton<IRepository<SchemataProcessToken>>(BpmnEngine_MultiInstanceShould.CreateRepository(Tokens).Object);
            services.AddSingleton<IRepository<SchemataProcessTransition>>(BpmnEngine_MultiInstanceShould.CreateRepository(Transitions).Object);
            services.AddSingleton<IRepository<SchemataProcessSource>>(BpmnEngine_MultiInstanceShould.CreateRepository(Sources).Object);
            services.AddSingleton<IRepository<SourceEntity>>(BpmnEngine_MultiInstanceShould.CreateRepository(SourceEntities).Object);
            Services = services.BuildServiceProvider();
            Engine   = new();
        }

        public ProcessDefinition Definition { get; }

        public BpmnEngine Engine { get; }

        public List<SchemataProcessToken> Tokens { get; }

        public List<SchemataProcessTransition> Transitions { get; }

        public List<SchemataProcessSource> Sources { get; }

        public List<SourceEntity> SourceEntities { get; }

        private ServiceProvider Services { get; }

        public static Harness Create(ProcessDefinition definition) { return new(definition); }

        public async Task<ProcessSnapshot> StartAsync() {
            var process = new SchemataProcess {
                Name           = "p1",
                CanonicalName  = "processes/p1",
                DefinitionName = Definition.Name,
            };

            var snapshot = await Engine.StartAsync(Definition, process, Context(), CancellationToken.None);
            Upsert(snapshot);
            return snapshot;
        }

        public async Task<ProcessSnapshot> CompleteSiblingsAsync(ProcessSnapshot snapshot, int count) {
            var current = snapshot;
            for (var i = 0; i < count; i++) {
                var sibling = NextSibling(current);
                current = await Engine.AdvanceAsync(Definition, current.Process, current.Tokens, Context(), sibling.CanonicalName, CancellationToken.None);
                Upsert(current);
            }

            return current;
        }

        public async Task<ProcessSnapshot> CompleteSiblingAsync(ProcessSnapshot snapshot, int counter) {
            var sibling = snapshot.Tokens.Single(t => t.Spawner is not null && t.State == "Active" && ReadInt(t, "loopCounter") == counter);
            var current = await Engine.AdvanceAsync(Definition, snapshot.Process, snapshot.Tokens, Context(), sibling.CanonicalName, CancellationToken.None);
            Upsert(current);
            return current;
        }

        public void BindSources(ProcessSnapshot snapshot, Func<int, string> name) {
            foreach (var sibling in snapshot.Tokens.Where(t => t.Spawner is not null)) {
                var source = new SourceEntity {
                    Name          = name(ReadInt(sibling, "loopCounter")),
                    CanonicalName = $"orders/{sibling.Name}",
                };
                SourceEntities.Add(source);
                Sources.Add(new() {
                    Process    = snapshot.Process.CanonicalName!,
                    Token      = sibling.CanonicalName,
                    Name       = "order",
                    SourceType = typeof(SourceEntity).FullName!,
                    Source     = source.CanonicalName!,
                });
            }
        }

        private FlowExecutionContext Context() { return new(new Mock<IUnitOfWork>(MockBehavior.Strict).Object, Services); }

        private static SchemataProcessToken NextSibling(ProcessSnapshot snapshot) {
            return snapshot.Tokens.Where(t => t.Spawner is not null && t.State == "Active")
                           .OrderBy(t => ReadInt(t, "loopCounter"))
                           .First();
        }

        private void Upsert(ProcessSnapshot snapshot) {
            foreach (var token in snapshot.Tokens) {
                BpmnEngine_MultiInstanceShould.Upsert(Tokens, token, t => t.CanonicalName);
            }

            foreach (var transition in snapshot.Transitions) {
                if (Transitions.All(row => row.Name != transition.Name)) {
                    Transitions.Add(transition);
                }
            }
        }
    }

    private static Mock<IRepository<TEntity>> CreateRepository<TEntity>(List<TEntity> rows)
        where TEntity : class {
        var repository = new Mock<IRepository<TEntity>>(MockBehavior.Strict);
        repository.Setup(value => value.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(value => value.FirstOrDefaultAsync<TEntity>(
                      It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>>? predicate, CancellationToken _) =>
                      ValueTask.FromResult<TEntity?>(
                          (predicate is null ? rows.AsQueryable() : predicate(rows.AsQueryable())).FirstOrDefault()));
        repository.Setup(value => value.SingleOrDefaultAsync<TEntity>(
                      It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>>? predicate, CancellationToken _) =>
                      ValueTask.FromResult<TEntity?>(
                          (predicate is null ? rows.AsQueryable() : predicate(rows.AsQueryable())).SingleOrDefault()));
        repository.Setup(value => value.ListAsync<TEntity>(
                      It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                      It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TEntity>, IQueryable<TEntity>>? predicate, CancellationToken ct) =>
                      ToAsync<TEntity>(predicate is null ? rows : predicate(rows.AsQueryable()), ct));
        repository.Setup(value => value.AddAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Callback<TEntity, CancellationToken>((entity, _) => rows.Add(entity))
                  .Returns(Task.CompletedTask);
        repository.Setup(value => value.UpdateAsync(It.IsAny<TEntity>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        return repository;
    }

    private static async IAsyncEnumerable<TEntity> ToAsync<TEntity>(
        IEnumerable<TEntity> rows,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var row in rows.ToList()) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private static void Upsert<TEntity>(List<TEntity> rows, TEntity entity, Func<TEntity, string?> key) {
        var existing = rows.FirstOrDefault(row => string.Equals(key(row), key(entity), StringComparison.Ordinal));
        if (existing is not null) {
            rows.Remove(existing);
        }

        rows.Add(entity);
    }

    public sealed class SourceEntity : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
