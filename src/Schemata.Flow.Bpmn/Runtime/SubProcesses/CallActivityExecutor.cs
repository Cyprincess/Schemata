using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
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
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public sealed class CallActivityExecutor<TProcess, TToken, TTransition>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    private readonly IServiceProvider _services;

    /// <summary>Creates an executor that resolves registry and repositories from <paramref name="services" />.</summary>
    public CallActivityExecutor(IServiceProvider services) {
        _services    = services;
    }

    /// <summary>
    ///     Enters a call activity by parking the parent token, starting the called process with the
    ///     engine keyed on the child registration, and persisting the spawned child process rows and
    ///     parent spawn transition.
    /// </summary>
    public async ValueTask<SchemataProcessTransition> EnterAsync(
        SchemataProcess      parentProcess,
        SchemataProcessToken parentToken,
        List<SchemataProcessToken> working,
        CallActivity         callActivity,
        FlowExecutionContext context,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(parentProcess);
        ArgumentNullException.ThrowIfNull(parentToken);
        ArgumentNullException.ThrowIfNull(callActivity);

        var registry = _services.GetService<IProcessRegistry>();
        if (registry is null) {
            throw new FailedPreconditionException(
                SchemataResources.BPMN_CALL_ACTIVITY_REQUIRES_SERVICES,
                new Dictionary<string, string?> { ["name"] = callActivity.Name });
        }

        var registration = registry.GetRegistration(callActivity.CalledElement, callActivity.DefinitionVersion);
        if (registration is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = callActivity.CalledElement }
            );
        }

        var waiter = TokenFactory<TProcess, TToken>.NewChildToken((TProcess)parentProcess, new(callActivity.Name, callActivity.Name, false), (TToken)parentToken);
        waiter.Bookkeeping = new(parentToken.Bookkeeping, StringComparer.Ordinal);
        await context.CreateTokenAsync(waiter, ct);
        working.Add(waiter);
        parentToken.State = "Completed";
        parentToken.StateName = callActivity.Name;
        parentToken.WaitingAtName = null;

        var childProcess = new TProcess {
            DefinitionName = registration.Name,
            DefinitionVersion = registration.Version,
        };
        await context.CreateProcessAsync(childProcess, ct);

        var childContext = new FlowExecutionContext(context.UnitOfWork, context.Services) {
            CreateProcessAsync = context.CreateProcessAsync,
            CreateTokenAsync = context.CreateTokenAsync,
            PersistSnapshotAsync = context.PersistSnapshotAsync,
            Principal = context.Principal,
            FindTokenAsync = context.FindTokenAsync,
            FindSourceBinding = context.FindSourceBinding,
            NewSourceBinding = context.NewSourceBinding,
            FindPersistedSourceAsync = context.FindPersistedSourceAsync,
            ListSourceBindingsAsync = context.ListSourceBindingsAsync,
            CreateSourceBindingAsync = context.CreateSourceBindingAsync,
            UpdateSourceBindingAsync = context.UpdateSourceBindingAsync,
            TrackSourceBinding = context.TrackSourceBinding,
            ValidateSourceAsync = context.ValidateSourceAsync,
            InitialSourceStamps = context.InitialSourceStamps,
            TouchedSources = context.TouchedSources,
        };
        var childEngine = _services.GetKeyedService<IFlowRuntime<TProcess, TToken, TTransition>>(registration.Engine);
        if (childEngine is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                new Dictionary<string, string?> { ["engine"] = registration.Engine });
        }

        var childSnapshot = await childEngine.StartAsync(registration.Definition, childProcess, childContext, ct);
        var spawn = NewTransition(
            parentProcess.Name!,
            waiter.CanonicalName,
            callActivity.Name,
            childProcess.CanonicalName,
            TransitionKind.Spawn,
            "CallActivity");
        spawn.Note = callActivity.Name;

        var activation               = Guid.NewGuid().ToString("n");
        spawn.Activation             = activation;
        waiter.CurrentCallActivation = activation;

        await context.PersistSnapshotAsync(childSnapshot, ct);

        return spawn;
    }

    /// <summary>
    ///     Reads the parent spawn transition identified by the call <paramref name="activation" /> and
    ///     returns terminal child completion details when the child process has no live tokens left. A
    ///     completion naming a call activation other than the token's current one is rejected.
    /// </summary>
    public async ValueTask<CallActivityCompletion?> TryCompleteAsync(
        SchemataProcess      parentProcess,
        SchemataProcessToken parentToken,
        string?              activation,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(parentProcess);
        ArgumentNullException.ThrowIfNull(parentToken);

        if (string.IsNullOrEmpty(activation)
         || !string.Equals(parentToken.CurrentCallActivation, activation, StringComparison.Ordinal)) {
            return null;
        }

        var transitions = _services.GetRequiredService<IRepository<TTransition>>();
        var spawn = await transitions.FirstOrDefaultAsync(
            q => q.Where(t => t.Process == parentProcess.Name
                           && t.Token == parentToken.CanonicalName
                           && t.Kind == TransitionKind.Spawn
                           && t.Activation == activation), ct);
        if (spawn?.Posterior is null) {
            return null;
        }

        var processes = _services.GetRequiredService<IRepository<TProcess>>();
        var child = await processes.FirstOrDefaultAsync(q => q.Where(p => p.CanonicalName == spawn.Posterior), ct);
        if (child is null || !TokenStates.IsTerminal(child.State)) {
            return null;
        }

        var tokens = _services.GetRequiredService<IRepository<TToken>>();
        var childTokens = new List<SchemataProcessToken>();
        await foreach (var token in tokens.ListAsync<TToken>(q => q.Where(t => t.Process == child.Name), ct)) {
            childTokens.Add(token);
        }

        if (childTokens.Any(t => !TokenStates.IsTerminal(t.State))) {
            return null;
        }

        var failed = string.Equals(child.State, "Failed", StringComparison.OrdinalIgnoreCase)
                  || childTokens.Any(t => string.Equals(t.State, "Failed", StringComparison.OrdinalIgnoreCase));

        parentToken.CurrentCallActivation = null;

        return new(
            child.CanonicalName ?? string.Empty,
            failed ? "Failed" : child.State!,
            failed);
    }


    private static SchemataProcessTransition NewTransition(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) => TransitionFactory<TTransition>.New(processName, tokenCanonical, previous, posterior, kind, eventName);
}