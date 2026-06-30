using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn;

internal static class BpmnEngineTestExtensions
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    internal static ValueTask<ProcessSnapshot> StartAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        CancellationToken ct = default
    ) {
        return engine.StartAsync(definition, process, Context(), ct);
    }

    internal static ValueTask<ProcessSnapshot> TriggerAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        IReadOnlyList<SchemataProcessToken> tokens,
        IEventDefinition trigger,
        object? payload,
        string? tokenName = null,
        CancellationToken ct = default
    ) {
        return engine.TriggerAsync(definition, process, tokens, Context(), trigger, payload, tokenName, ct);
    }

    internal static ValueTask<ProcessSnapshot> AdvanceAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        IReadOnlyList<SchemataProcessToken> tokens,
        string? tokenName = null,
        CancellationToken ct = default
    ) {
        return engine.AdvanceAsync(definition, process, tokens, Context(), tokenName, ct);
    }

    private static FlowExecutionContext Context() {
        return new(new TestUnitOfWork(), Services);
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }

        public void Dispose() { }

        public Task CommitAsync(CancellationToken ct = default) { return Task.CompletedTask; }

        public Task RollbackAsync(CancellationToken ct = default) { return Task.CompletedTask; }
    }
}
