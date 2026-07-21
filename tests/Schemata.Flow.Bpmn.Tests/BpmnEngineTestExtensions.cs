using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn;

internal static class BpmnEngineTestExtensions
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();
    private static readonly ConditionalWeakTable<SchemataProcess, CompensationState> CompensationBindings = new();

    internal static async ValueTask<ProcessSnapshot> StartAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        CancellationToken ct = default
    ) {
        var snapshot = await engine.StartAsync(definition, process, Context(process), ct);
        Store(snapshot);
        return snapshot;
    }

    internal static async ValueTask<ProcessSnapshot> TriggerAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        IReadOnlyList<SchemataProcessToken> tokens,
        IEventDefinition trigger,
        object? payload,
        string? tokenName = null,
        CancellationToken ct = default
    ) {
        var snapshot = await engine.TriggerAsync(definition, process, tokens, Context(process), trigger, payload, tokenName, ct);
        Store(snapshot);
        return snapshot;
    }

    internal static async ValueTask<ProcessSnapshot> AdvanceAsync(
        this BpmnEngine engine,
        ProcessDefinition definition,
        SchemataProcess process,
        IReadOnlyList<SchemataProcessToken> tokens,
        string? tokenName = null,
        CancellationToken ct = default
    ) {
        var snapshot = await engine.AdvanceAsync(definition, process, tokens, Context(process), tokenName, ct);
        Store(snapshot);
        return snapshot;
    }

    private static FlowExecutionContext Context(SchemataProcess process) {
        return new(Mock.Of<IUnitOfWork>(), Services) {
            LoadedCompensationBindings = CompensationBindings.TryGetValue(process, out var state) ? state.Bindings : [],
        };
    }

    internal static FlowExecutionContext Context(IReadOnlyList<ProcessCompensationBinding> bindings) {
        return new(Mock.Of<IUnitOfWork>(), Services) { LoadedCompensationBindings = bindings };
    }

    private static void Store(ProcessSnapshot snapshot) {
        CompensationBindings.Remove(snapshot.Process);
        CompensationBindings.Add(snapshot.Process, new(snapshot.CompensationBindings));
    }

    private sealed record CompensationState(IReadOnlyList<ProcessCompensationBinding> Bindings);

}
