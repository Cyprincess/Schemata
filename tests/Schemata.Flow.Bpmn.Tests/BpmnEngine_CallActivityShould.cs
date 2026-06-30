using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
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

        var child = Assert.Single(harness.Processes.Rows, p => p.DefinitionName == "called");
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

        var transition = Assert.Single(harness.Transitions.Rows, t => t.Process == snapshot.Process.Name
                                                       && t.Kind == TransitionKind.Spawn);
        var child = Assert.Single(harness.Processes.Rows, p => p.DefinitionName == "called");
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

            Processes   = new();
            Tokens      = new();
            Transitions = new();
            Registry = new(
                Registration("parent", ParentDefinition),
                Registration("called", CalledDefinition));

            var services = new ServiceCollection();
            services.AddSingleton<IProcessRegistry>(Registry);
            services.AddSingleton<IRepository<SchemataProcess>>(Processes);
            services.AddSingleton<IRepository<SchemataProcessToken>>(Tokens);
            services.AddSingleton<IRepository<SchemataProcessTransition>>(Transitions);
            Services = services.BuildServiceProvider();
            Engine   = new();
        }

        public ProcessDefinition ParentDefinition { get; }

        public ProcessDefinition CalledDefinition { get; }

        public BpmnEngine Engine { get; }

        public TestRepository<SchemataProcess> Processes { get; }

        public TestRepository<SchemataProcessToken> Tokens { get; }

        public TestRepository<SchemataProcessTransition> Transitions { get; }

        public TestProcessRegistry Registry { get; }

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
            var child = Processes.Rows.Single(p => p.DefinitionName == "called");
            var childTokens = Tokens.Rows.Where(t => t.Process == child.Name).ToList();
            var completed = await Engine.AdvanceAsync(CalledDefinition, child, childTokens, Context(), childTokens[0].CanonicalName, CancellationToken.None);
            Processes.Upsert(completed.Process, p => p.CanonicalName);
            foreach (var token in completed.Tokens) {
                Tokens.Upsert(token, t => t.CanonicalName);
            }

            foreach (var transition in completed.Transitions) {
                Transitions.Rows.Add(transition);
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
            var child = Processes.Rows.Single(p => p.DefinitionName == "called");
            child.State     = "Failed";
            foreach (var token in Tokens.Rows.Where(t => t.Process == child.Name)) {
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

        private FlowExecutionContext Context() { return new(new TestUnitOfWork(), Services); }
    }

    private sealed class TestProcessRegistry(params ProcessRegistration[] registrations) : IProcessRegistry
    {
        private readonly Dictionary<string, ProcessRegistration> _registrations = registrations.ToDictionary(r => r.Name, StringComparer.Ordinal);

        public ValueTask RegisterAsync<TProcess>(string? engine = null, Action<ProcessConfiguration>? configure = null, CancellationToken ct = default)
            where TProcess : ProcessDefinition {
            throw new NotSupportedException();
        }

        public ValueTask RegisterAsync(ProcessConfiguration configuration, CancellationToken ct = default) { throw new NotSupportedException(); }

        public ValueTask UnregisterAsync(string processName, CancellationToken ct = default) {
            _registrations.Remove(processName);
            return default;
        }

        public IReadOnlyCollection<string> GetRegisteredProcesses() { return _registrations.Keys.ToList(); }

        public bool IsRegistered(string processName) { return _registrations.ContainsKey(processName); }

        public ProcessRegistration? GetRegistration(string processName) {
            _registrations.TryGetValue(processName, out var registration);
            return registration;
        }
    }

    private sealed class TestRepository<TEntity> : IRepository<TEntity>
        where TEntity : class
    {
        public List<TEntity> Rows { get; } = [];

        public AdviceContext AdviceContext { get; } = new(new ServiceCollection().BuildServiceProvider());

        public IUnitOfWork Begin() { return new TestUnitOfWork(); }

        public void Join(IUnitOfWork uow) { }

        public Task CommitAsync(CancellationToken ct = default) { return Task.CompletedTask; }

        public IDisposable SuppressAddValidation() { return NoopDisposable.Instance; }

        public IDisposable SuppressUpdateValidation() { return NoopDisposable.Instance; }

        public IDisposable SuppressQuerySoftDelete() { return NoopDisposable.Instance; }

        public IDisposable SuppressSoftDelete() { return NoopDisposable.Instance; }

        public IDisposable SuppressTimestamp() { return NoopDisposable.Instance; }

        public ValueTask DisposeAsync() { return default; }

        public void Dispose() { }

        public async IAsyncEnumerable<TResult> ListAsync<TResult>(
            Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
            [EnumeratorCancellation] CancellationToken ct = default
        ) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            foreach (var item in query.ToList()) {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default) { return new(entity); }

        public ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default) { return new(entity is TResult result ? result : default); }

        public ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default) { return new(Rows.FirstOrDefault()); }

        public ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) { return new(Rows.OfType<TResult>().FirstOrDefault()); }

        public ValueTask<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            return new(query.FirstOrDefault());
        }

        public ValueTask<TResult?> SingleOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            return new(query.SingleOrDefault());
        }

        public ValueTask<bool> AnyAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            return new(query.Any());
        }

        public ValueTask<int> CountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            return new(query.Count());
        }

        public ValueTask<long> LongCountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
            var query = predicate is null ? Rows.AsQueryable().OfType<TResult>() : predicate(Rows.AsQueryable());
            return new(query.LongCount());
        }

        public Task AddAsync(TEntity entity, CancellationToken ct = default) {
            Rows.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
            Rows.AddRange(entities);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(TEntity entity, CancellationToken ct = default) { return Task.CompletedTask; }

        public Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
            Rows.Remove(entity);
            return Task.CompletedTask;
        }

        public Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
            foreach (var entity in entities) {
                Rows.Remove(entity);
            }

            return Task.CompletedTask;
        }

        public void Upsert(TEntity entity, Func<TEntity, string?> key) {
            var existing = Rows.FirstOrDefault(row => string.Equals(key(row), key(entity), StringComparison.Ordinal));
            if (existing is not null) {
                Rows.Remove(existing);
            }

            Rows.Add(entity);
        }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public ValueTask DisposeAsync() { return default; }

        public void Dispose() { }

        public Task CommitAsync(CancellationToken ct = default) { return Task.CompletedTask; }

        public Task RollbackAsync(CancellationToken ct = default) { return Task.CompletedTask; }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose() { }
    }
}
