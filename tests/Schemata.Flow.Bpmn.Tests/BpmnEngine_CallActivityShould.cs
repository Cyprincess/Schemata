using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_CallActivityShould
{
    [Fact]
    public async Task Start_ParentWithCallActivity_SpawnsChildProcessRow() {
        var harness = Harness.Create();

        await harness.StartParentAsync();

        var child = Assert.Single(harness.Processes, p => p.DefinitionName == "called");
        Assert.Equal("called", child.DefinitionName);
        Assert.StartsWith("processes/", child.CanonicalName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_ParentWithCallActivity_ParksParentTokenAtCallActivity() {
        var harness = Harness.Create();

        var snapshot = await harness.StartParentAsync();

        var parent = Assert.Single(snapshot.Tokens);
        Assert.Equal("Waiting", parent.State);
        Assert.Equal("call", parent.StateName);
        Assert.Equal("call", parent.WaitingAtName);
    }

    [Fact]
    public async Task Start_ParentWithCallActivity_WritesParentTransitionWithKindSpawn() {
        var harness = Harness.Create();

        var snapshot = await harness.StartParentAsync();

        var transition = Assert.Single(harness.Transitions, t => t.Process == snapshot.Process.Name
                                                       && t.Kind == TransitionKind.Spawn);
        var child = Assert.Single(harness.Processes, p => p.DefinitionName == "called");
        Assert.Equal(snapshot.Tokens[0].CanonicalName, transition.Token);
        Assert.Equal(child.CanonicalName, transition.Posterior);
        Assert.Equal("call", transition.Note);
    }

    [Fact]
    public async Task Complete_ChildProcess_ResumesParentTokenToOutgoingFlow() {
        var harness = Harness.Create();
        var parent  = await harness.StartParentAsync();
        await harness.CompleteChildAsync();

        var resumed = await harness.AdvanceParentAsync(parent);

        var token = Assert.Single(resumed.Tokens);
        Assert.Equal("Active", token.State);
        Assert.Equal("after-call", token.StateName);
        Assert.Null(token.WaitingAtName);
    }

    [Fact]
    public async Task Complete_ChildProcessWithFailure_PropagatesFailureToParent() {
        var harness = Harness.Create();
        var parent  = await harness.StartParentAsync();
        harness.FailChild();

        var failed = await harness.AdvanceParentAsync(parent);

        var token = Assert.Single(failed.Tokens);
        Assert.Equal("Failed", token.State);
        Assert.Equal("call", token.StateName);
        Assert.Equal("Failed", failed.Process.State);
        Assert.Contains(failed.Transitions, t => t.Kind == TransitionKind.Fail && t.Token == token.CanonicalName);
    }

    private static ProcessDefinition ParentDefinition() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var call     = new CallActivity { Name = "call", CalledElement = "called" };
        var after    = new NoneTask { Name = "after-call" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "parent",
            Elements = { start, call, after, endEvent },
            Flows = {
                new() { Source = start, Target = call },
                new() { Source = call, Target = after },
                new() { Source = after, Target = endEvent },
            },
        };
    }

    private static ProcessDefinition CalledDefinition() {
        var start    = new FlowEvent { Name = "child-start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "child-task" };
        var endEvent = new FlowEvent { Name = "child-end", Position = EventPosition.End };

        return new() {
            Name     = "called",
            Elements = { start, task, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
            },
        };
    }

    private sealed class Harness
    {
        private Harness() {
            ParentDefinition = ParentDefinition();
            CalledDefinition = CalledDefinition();

            Processes   = [];
            Tokens      = [];
            Transitions = [];
            var registry = new[] {
                Registration("parent", ParentDefinition),
                Registration("called", CalledDefinition),
            };
            var processRegistry = new Mock<IProcessRegistry>();
            processRegistry.Setup(r => r.GetRegistration(It.IsAny<string>()))
                           .Returns((string name) => registry.FirstOrDefault(r => r.Name == name));
            processRegistry.Setup(r => r.IsRegistered(It.IsAny<string>()))
                           .Returns((string name) => registry.Any(r => r.Name == name));

            var services = new ServiceCollection();
            services.AddSingleton<IProcessRegistry>(processRegistry.Object);
            services.AddSingleton<IRepository<SchemataProcess>>(CreateRepository(Processes).Object);
            services.AddSingleton<IRepository<SchemataProcessToken>>(CreateRepository(Tokens).Object);
            services.AddSingleton<IRepository<SchemataProcessTransition>>(CreateRepository(Transitions).Object);
            Services = services.BuildServiceProvider();
            Engine   = new();
        }

        public ProcessDefinition ParentDefinition { get; }

        public ProcessDefinition CalledDefinition { get; }

        public BpmnEngine Engine { get; }

        public List<SchemataProcess> Processes { get; }

        public List<SchemataProcessToken> Tokens { get; }

        public List<SchemataProcessTransition> Transitions { get; }

        private ServiceProvider Services { get; }

        public static Harness Create() { return new(); }

        public async Task<ProcessSnapshot> StartParentAsync() {
            var process = new SchemataProcess {
                Name           = "p1",
                CanonicalName  = "processes/p1",
                DefinitionName = "parent",
            };

            return await Engine.StartAsync(ParentDefinition, process, Context(), CancellationToken.None);
        }

        public async Task CompleteChildAsync() {
            var child = Processes.Single(p => p.DefinitionName == "called");
            var childTokens = Tokens.Where(t => t.Process == child.Name).ToList();
            var completed = await Engine.AdvanceAsync(CalledDefinition, child, childTokens, Context(), childTokens[0].CanonicalName, CancellationToken.None);
            Upsert(Processes, completed.Process, p => p.CanonicalName);
            foreach (var token in completed.Tokens) {
                Upsert(Tokens, token, t => t.CanonicalName);
            }

            foreach (var transition in completed.Transitions) {
                Transitions.Add(transition);
            }
        }

        public async Task<ProcessSnapshot> AdvanceParentAsync(ProcessSnapshot parent) {
            return await Engine.AdvanceAsync(
                ParentDefinition,
                parent.Process,
                parent.Tokens,
                Context(),
                parent.Tokens[0].CanonicalName,
                CancellationToken.None);
        }

        public void FailChild() {
            var child = Processes.Single(p => p.DefinitionName == "called");
            child.State     = "Failed";
            foreach (var token in Tokens.Where(t => t.Process == child.Name)) {
                token.State       = "Failed";
                token.WaitingAtName = null;
            }
        }

        private static ProcessRegistration Registration(string name, ProcessDefinition definition) {
            return new() {
                Name          = name,
                Engine        = SchemataConstants.FlowEngines.Bpmn,
                Definition    = definition,
                Configuration = new() { Name = name, Engine = SchemataConstants.FlowEngines.Bpmn },
            };
        }

        private FlowExecutionContext Context() { return new(new Mock<IUnitOfWork>(MockBehavior.Strict).Object, Services); }

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
    }

}
