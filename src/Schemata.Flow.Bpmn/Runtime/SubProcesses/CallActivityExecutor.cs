using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Runtime.SubProcesses;

/// <summary>
///     Executes BPMN <see cref="CallActivity" /> entry and resume semantics by invoking a
///     registered child <see cref="ProcessDefinition" /> from the same <see cref="IProcessRegistry" />.
///     Uses the flow unit of work when starting and resuming the called process.
/// </summary>
public sealed class CallActivityExecutor
{
    private readonly IServiceProvider _services;
    private readonly IUnitOfWork      _unitOfWork;

    /// <summary>Creates an executor that resolves registry and repositories from <paramref name="services" />.</summary>
    public CallActivityExecutor(IServiceProvider services, IUnitOfWork unitOfWork) {
        _services    = services;
        _unitOfWork  = unitOfWork;
    }

    /// <summary>
    ///     Enters a call activity by parking the parent token, starting the called process with the
    ///     same BPMN engine, and persisting the spawned child process rows and parent spawn transition.
    /// </summary>
    public async ValueTask<SchemataProcessTransition> EnterAsync(
        BpmnEngine           engine,
        SchemataProcess      parentProcess,
        SchemataProcessToken parentToken,
        CallActivity         callActivity,
        FlowExecutionContext context,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(parentProcess);
        ArgumentNullException.ThrowIfNull(parentToken);
        ArgumentNullException.ThrowIfNull(callActivity);

        var registry = _services.GetService<IProcessRegistry>();
        if (registry is null) {
            throw new FailedPreconditionException(
                SchemataResources.BPMN_CALL_ACTIVITY_REQUIRES_SERVICES,
                new Dictionary<string, string?> { ["name"] = callActivity.Name });
        }

        var registration = registry.GetRegistration(callActivity.CalledElement);
        if (registration is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = callActivity.CalledElement }
            );
        }

        parentToken.State       = "Waiting";
        parentToken.StateName     = callActivity.Name;
        parentToken.WaitingAtName = callActivity.Name;

        var leaf = Identifiers.NewUid().ToString("n");
        var childProcess = new SchemataProcess {
            Name           = leaf,
            CanonicalName  = $"processes/{leaf}",
            DefinitionName = registration.Name,
        };

        var childSnapshot = await engine.StartAsync(registration.Definition, childProcess, context, ct);
        var spawn = NewTransition(
            parentProcess.Name!,
            parentToken.CanonicalName,
            callActivity.Name,
            childProcess.CanonicalName,
            TransitionKind.Spawn,
            "CallActivity");
        spawn.Note = callActivity.Name;

        await PersistSpawnAsync(childSnapshot, spawn, ct);

        return spawn;
    }

    /// <summary>
    ///     Reads the parent spawn transition and returns terminal child completion details when the
    ///     child process has no live tokens left.
    /// </summary>
    public async ValueTask<CallActivityCompletion?> TryCompleteAsync(
        SchemataProcess      parentProcess,
        SchemataProcessToken parentToken,
        CallActivity         callActivity,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(parentProcess);
        ArgumentNullException.ThrowIfNull(parentToken);
        ArgumentNullException.ThrowIfNull(callActivity);

        var transitions = _services.GetRequiredService<IRepository<SchemataProcessTransition>>();
        var spawn = await transitions.FirstOrDefaultAsync(
            q => q.Where(t => t.Process == parentProcess.Name
                           && t.Token == parentToken.CanonicalName
                           && t.Kind == TransitionKind.Spawn
                           && t.Note == callActivity.Name), ct);
        if (spawn?.Posterior is null) {
            return null;
        }

        var processes = _services.GetRequiredService<IRepository<SchemataProcess>>();
        var child = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == spawn.Posterior), ct);
        if (child is null || !TokenStates.IsTerminal(child.State)) {
            return null;
        }

        var tokens = _services.GetRequiredService<IRepository<SchemataProcessToken>>();
        var childTokens = new List<SchemataProcessToken>();
        await foreach (var token in tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.Process == child.Name), ct)) {
            childTokens.Add(token);
        }

        if (childTokens.Any(t => !TokenStates.IsTerminal(t.State))) {
            return null;
        }

        var failed = string.Equals(child.State, "Failed", StringComparison.OrdinalIgnoreCase)
                  || childTokens.Any(t => string.Equals(t.State, "Failed", StringComparison.OrdinalIgnoreCase));

        return new(
            child.CanonicalName ?? string.Empty,
            failed ? "Failed" : child.State!,
            failed);
    }

    private async Task PersistSpawnAsync(
        ProcessSnapshot            childSnapshot,
        SchemataProcessTransition  parentSpawn,
        CancellationToken          ct
    ) {
        var processes   = _services.GetRequiredService<IRepository<SchemataProcess>>();
        var tokens      = _services.GetRequiredService<IRepository<SchemataProcessToken>>();
        var transitions = _services.GetRequiredService<IRepository<SchemataProcessTransition>>();

        processes.Join(_unitOfWork);
        tokens.Join(_unitOfWork);
        transitions.Join(_unitOfWork);

        await UpsertProcessAsync(processes, childSnapshot.Process, ct);

        foreach (var token in childSnapshot.Tokens) {
            await UpsertTokenAsync(tokens, token, ct);
        }

        foreach (var transition in childSnapshot.Transitions) {
            await transitions.AddAsync(transition, ct);
        }

        await transitions.AddAsync(parentSpawn, ct);
    }

    private static async Task UpsertProcessAsync(
        IRepository<SchemataProcess> processes,
        SchemataProcess              process,
        CancellationToken            ct
    ) {
        var existing = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == process.CanonicalName), ct);
        if (existing is null) {
            await processes.AddAsync(process, ct);
            return;
        }

        existing.DefinitionName = process.DefinitionName;
        existing.State          = process.State;
        await processes.UpdateAsync(existing, ct);
    }

    private static async Task UpsertTokenAsync(
        IRepository<SchemataProcessToken> tokens,
        SchemataProcessToken              token,
        CancellationToken                 ct
    ) {
        var existing = await tokens.FirstOrDefaultAsync(q => q.Where(t => t.CanonicalName == token.CanonicalName), ct);
        if (existing is null) {
            await tokens.AddAsync(token, ct);
            return;
        }

        existing.Process     = token.Process;
        existing.Spawner     = token.Spawner;
        existing.ScopeName     = token.ScopeName;
        existing.StateName     = token.StateName;
        existing.WaitingAtName = token.WaitingAtName;
        existing.Bookkeeping = new(token.Bookkeeping);
        existing.State       = token.State;
        await tokens.UpdateAsync(existing, ct);
    }

    private static SchemataProcessTransition NewTransition(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) => TransitionFactory.New(processName, tokenCanonical, previous, posterior, kind, eventName);
}

/// <summary>Terminal child-process status observed when a <see cref="CallActivity" /> can resume its parent token.</summary>
public sealed record CallActivityCompletion(
    string  ChildProcess,
    string  State,
    bool    Failed);
