using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Executes Flow runtime operations and persists their results.</summary>
public sealed class FlowRunner(
    IProcessRegistry                registry,
    ProcessPersistence              persistence,
    ProcessLifecycleNotifier        notifier,
    IServiceProvider                services,
    IServiceScopeFactory            scopes,
    IOptions<SchemataFlowOptions>   options
) : IFlowRunner
{
    private static readonly ConcurrentDictionary<Type, ISourceWorker?> SourceWorkers = new();

    #region IFlowRunner Members

    public ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        return StartCoreAsync(definitionName, options, source, source.CanonicalName, null, ct);
    }

    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options = null,
        CancellationToken    ct      = default
    ) {
        return StartCoreAsync<object>(definitionName, options, null, null, null, ct);
    }

    #endregion

    /// <summary>Starts a process from a resource request.</summary>
    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        string?              source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        return StartCoreAsync<object>(definitionName, options, null, source, principal, ct);
    }

    /// <summary>Starts a process from a resource request without a source entity.</summary>
    public ValueTask<SchemataProcess> StartAsync(
        string               definitionName,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        return StartCoreAsync<object>(definitionName, options, null, null, principal, ct);
    }

    /// <summary>Starts a process from a resource request and binds a loaded source entity.</summary>
    public ValueTask<SchemataProcess> StartAsync<TState>(
        string               definitionName,
        TState               source,
        StartProcessOptions? options,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) where TState : class, ICanonicalName {
        if (string.IsNullOrEmpty(source.CanonicalName)) {
            throw new InvalidOperationException($"Source entity type '{typeof(TState).FullName}' has no canonical name.");
        }

        return StartCoreAsync(definitionName, options, source, source.CanonicalName, principal, ct);
    }

    /// <summary>Completes the addressed token on a process.</summary>
    public async ValueTask<ProcessSnapshot> CompleteAsync(
        SchemataProcess  process,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg    = ResolveRegistration(process.DefinitionName);
        var engine = ResolveEngine(reg);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens = await LoadTokensAsync(scope, process.Name!, c);
            var before = WaitingMap(tokens);
            var ctx    = await CreateExecutionContextAsync(scope, process, principal, c);
            snapshot = await engine.AdvanceAsync(reg.Definition, process, tokens, ctx, token, c);
            EnsureCatchesHaveHandlers(reg.Definition, snapshot);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    /// <summary>Correlates a message to the process.</summary>
    public async ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess  process,
        string           messageName,
        string?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg   = ResolveRegistration(process.DefinitionName);
        var value = DeserializePayload(payload, reg.MessagePayloadTypes.GetValueOrDefault(messageName));
        return await CorrelateCoreAsync(process, reg, messageName, value, token, principal, ct);
    }

    /// <summary>Correlates a typed message payload to the process.</summary>
    public ValueTask<ProcessSnapshot> CorrelateAsync(
        SchemataProcess  process,
        string           messageName,
        object?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg = ResolveRegistration(process.DefinitionName);
        return CorrelateCoreAsync(process, reg, messageName, payload, token, principal, ct);
    }

    private async ValueTask<ProcessSnapshot> CorrelateCoreAsync(
        SchemataProcess      process,
        ProcessRegistration  reg,
        string               messageName,
        object?              payload,
        string?              token,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var engine  = ResolveEngine(reg);
        var message = reg.Definition.Messages.FirstOrDefault(m => m.Name == messageName);
        if (message is null) {
            throw new InvalidArgumentException(
                SchemataResources.PROCESS_MESSAGE_NOT_DEFINED,
                new Dictionary<string, string?> { ["name"] = messageName }
            );
        }

        return await TriggerAddressedAsync(process, reg, engine, message, payload, token, resolveTarget: true, principal, ct);
    }

    /// <summary>
    ///     Triggers the token addressed by <paramref name="tokenName" /> through the full transition
    ///     unit of work so infrastructure bridges never bypass the advisor chain or source write-back.
    /// </summary>
    public async ValueTask<ProcessSnapshot> RunEventAsync(
        string            processName,
        string?           tokenName,
        IEventDefinition  trigger,
        object?           payload,
        CancellationToken ct
    ) {
        ArgumentNullException.ThrowIfNull(trigger);

        var process = await persistence.FindAsync(services, processName, ct);
        if (process is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = processName }
            );
        }

        var reg    = ResolveRegistration(process.DefinitionName);
        var engine = ResolveEngine(reg);
        return await TriggerAddressedAsync(process, reg, engine, trigger, payload, tokenName, resolveTarget: false, principal: null, ct);
    }

    private async ValueTask<ProcessSnapshot> TriggerAddressedAsync(
        SchemataProcess     process,
        ProcessRegistration reg,
        IFlowRuntime        engine,
        IEventDefinition    trigger,
        object?             payload,
        string?             token,
        bool                resolveTarget,
        ClaimsPrincipal?    principal,
        CancellationToken   ct
    ) {
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens    = await LoadTokensAsync(scope, process.Name!, c);
            var ctx       = await CreateExecutionContextAsync(scope, process, principal, c);
            var tokenName = resolveTarget
                ? await ResolveTargetAsync(engine, reg.Definition, process, tokens, ctx, trigger, token, c)
                : token;
            var before = WaitingMap(tokens);
            snapshot = await engine.TriggerAsync(reg.Definition, process, tokens, ctx, trigger, payload, tokenName, c);
            EnsureCatchesHaveHandlers(reg.Definition, snapshot);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    /// <summary>Broadcasts a signal to waiting processes.</summary>
    public async ValueTask<IReadOnlyList<SignalDeliveryResult>> ThrowSignalAsync(
        string           signalName,
        string?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        return await ThrowSignalCoreAsync(signalName, payload, token, principal, ct, true);
    }

    /// <summary>Broadcasts a signal with a typed payload to waiting processes.</summary>
    public async ValueTask<IReadOnlyList<SignalDeliveryResult>> ThrowSignalAsync(
        string           signalName,
        object?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        return await ThrowSignalCoreAsync(signalName, payload, token, principal, ct, false);
    }

    private async ValueTask<IReadOnlyList<SignalDeliveryResult>> ThrowSignalCoreAsync(
        string           signalName,
        object?          payload,
        string?          token,
        ClaimsPrincipal? principal,
        CancellationToken ct,
        bool             deserialize
    ) {
        // Cancellation before the candidate set exists has nothing to report per target, so it
        // propagates. Once the snapshot is taken every candidate gets an entry instead.
        var candidates = await SnapshotSignalCandidatesAsync(signalName, ct);
        if (candidates.Count == 0) {
            return [];
        }

        var concurrency = Math.Max(1, options.Value.SignalBroadcastConcurrency);
        var results     = new SignalDeliveryResult?[candidates.Count];
        var pending     = new List<Task<(int Index, SignalDeliveryResult Result)>>(concurrency);

        using var gate = new SemaphoreSlim(concurrency, concurrency);
        try {
            for (var i = 0; i < candidates.Count; i++) {
                if (ct.IsCancellationRequested) {
                    results[i] = new(candidates[i].CanonicalName, SignalDeliveryStatus.Canceled);
                    continue;
                }

                try {
                    await gate.WaitAsync(ct);
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    results[i] = new(candidates[i].CanonicalName, SignalDeliveryStatus.Canceled);
                    continue;
                }

                pending.Add(DeliverInOwnScopeAsync(
                                i, candidates[i], signalName, payload, token, principal, deserialize, gate, ct));

                // Never hold more than the configured number of in-flight deliveries; this is what
                // bounds live scopes and units of work without awaiting the whole candidate set.
                if (pending.Count >= concurrency) {
                    await DrainOneAsync(pending, results);
                }
            }
        } finally {
            while (pending.Count > 0) {
                await DrainOneAsync(pending, results);
            }
        }

        return results.Select(static r => r!).ToList();
    }

    /// <summary>
    ///     Reads the waiting processes that declare <paramref name="signalName" /> into a detached,
    ///     stably ordered identity list. Discovery holds its own scope and is fully drained before
    ///     any delivery runs, so its reader never overlaps a delivery's writes.
    /// </summary>
    private async ValueTask<IReadOnlyList<SignalCandidate>> SnapshotSignalCandidatesAsync(
        string            signalName,
        CancellationToken ct
    ) {
        var candidates = new List<SignalCandidate>();

        await using (var scope = scopes.CreateAsyncScope()) {
            await foreach (var process in persistence.ListWaitingAsync(scope.ServiceProvider, ct)) {
                if (string.IsNullOrEmpty(process.CanonicalName)) {
                    continue;
                }

                var reg = registry.GetRegistration(process.DefinitionName);
                if (reg?.Definition.Signals.Any(s => s.Name == signalName) != true) {
                    continue;
                }

                candidates.Add(new(process.CanonicalName, process.DefinitionName));
            }
        }

        // ListWaitingAsync walks a HashSet and its query carries no ORDER BY, so the arrival order
        // is not reproducible; results are positional, so impose an order the caller can rely on.
        candidates.Sort(static (a, b) => string.CompareOrdinal(a.CanonicalName, b.CanonicalName));
        return candidates;
    }

    private async Task<(int Index, SignalDeliveryResult Result)> DeliverInOwnScopeAsync(
        int               index,
        SignalCandidate   candidate,
        string            signalName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        bool              deserialize,
        SemaphoreSlim     gate,
        CancellationToken ct
    ) {
        try {
            // One delivery, one scope, one unit of work: the delivery runner owns its repositories,
            // advisors, observers and AdviceContext, none of which are safe to share concurrently.
            await using var scope = scopes.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
            var result = await runner.DeliverSignalAsync(
                             candidate, signalName, payload, token, principal, deserialize, ct);
            return (index, result);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return (index, new(candidate.CanonicalName, SignalDeliveryStatus.Canceled));
        } catch (Exception ex) {
            return (index, new(candidate.CanonicalName, SignalDeliveryStatus.Failed, ex));
        } finally {
            gate.Release();
        }
    }

    private static async Task DrainOneAsync(
        List<Task<(int Index, SignalDeliveryResult Result)>> pending,
        SignalDeliveryResult?[]                              results
    ) {
        var completed = await Task.WhenAny(pending);
        pending.Remove(completed);

        // The worker converts every outcome into a result, so awaiting it cannot throw here.
        var (index, result) = await completed;
        results[index] = result;
    }

    /// <summary>Delivers a broadcast signal to one process, inside this runner's own scope.</summary>
    private async ValueTask<SignalDeliveryResult> DeliverSignalAsync(
        SignalCandidate   candidate,
        string            signalName,
        object?           payload,
        string?           token,
        ClaimsPrincipal?  principal,
        bool              deserialize,
        CancellationToken ct
    ) {
        var reg    = registry.GetRegistration(candidate.DefinitionName);
        var signal = reg?.Definition.Signals.FirstOrDefault(s => s.Name == signalName);
        if (reg is null || signal is null) {
            return new(candidate.CanonicalName, SignalDeliveryStatus.NoLongerWaiting);
        }

        var engine = ResolveEngine(reg);
        var value = deserialize && payload is string text
            ? DeserializePayload(text, reg.SignalPayloadTypes.GetValueOrDefault(signalName))
            : payload;

        var              delivered = false;
        var              committed = new List<ProcessSnapshot>();
        SchemataProcess? target    = null;

        try {
            await persistence.ExecuteAsync(services, async (scope, c) => {
                committed.Clear();
                delivered = false;

                // The candidate list carries identity only; the process itself is reloaded here so
                // it belongs to this delivery's unit of work.
                var process = await scope.Processes.FirstOrDefaultAsync(
                                  q => q.Where(p => p.CanonicalName == candidate.CanonicalName), c);
                if (process is null) {
                    return;
                }

                target = process;
                var tokens  = await LoadTokensAsync(scope, process.Name!, c);
                var ctx     = await CreateExecutionContextAsync(scope, process, principal, c);
                var targets = await engine.FindTriggerTargetsAsync(reg.Definition, process, tokens, ctx, signal, c);
                foreach (var item in FilterTargets(targets, token)) {
                    var before   = WaitingMap(tokens);
                    var snapshot = await engine.TriggerAsync(
                                       reg.Definition, process, tokens, ctx, signal, value, item, c);
                    EnsureCatchesHaveHandlers(reg.Definition, snapshot);
                    await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
                    await persistence.PersistSnapshotAsync(scope, snapshot, c);
                    committed.Add(snapshot);
                    delivered = true;
                }
            }, ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return new(candidate.CanonicalName, SignalDeliveryStatus.Canceled);
        } catch (Exception ex) {
            if (target is not null) {
                await notifier.NotifyFailedAsync(target, ex, CancellationToken.None);
            }

            return new(candidate.CanonicalName, SignalDeliveryStatus.Failed, ex);
        }

        if (!delivered) {
            return new(candidate.CanonicalName, SignalDeliveryStatus.NoLongerWaiting);
        }

        // IProcessLifecycleObserver is a post-persistence contract, so notify only after the unit of
        // work committed — a rollback must not leave observers believing the transition landed.
        foreach (var snapshot in committed) {
            await NotifyTransitionResultAsync(snapshot, ct);
        }

        return new(candidate.CanonicalName, SignalDeliveryStatus.Delivered);
    }

    private sealed record SignalCandidate(string CanonicalName, string DefinitionName);

    /// <summary>Terminates a process and cancels its tokens.</summary>
    public async ValueTask<ProcessSnapshot> TerminateAsync(
        SchemataProcess  process,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var reg = ResolveRegistration(process.DefinitionName);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens      = await LoadTokensAsync(scope, process.Name!, c);
            var before      = WaitingMap(tokens);
            var ctx         = await CreateExecutionContextAsync(scope, process, principal, c);
            var transitions = new List<SchemataProcessTransition>();
            foreach (var item in tokens) {
                var previous = item.WaitingAtName ?? item.StateName;
                item.State         = "Cancelled";
                item.WaitingAtName = null;

                transitions.Add(CancelTransition(process, item, previous, "Terminated", "Terminate", principal));
            }

            process.State = "Terminated";
            snapshot = new() { Process = process, Tokens = tokens, Transitions = transitions };
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await notifier.NotifyTransitionedAsync(snapshot!, ct);
        await notifier.NotifyTerminatedAsync(process, ct);
        return snapshot!;
    }

    /// <summary>Cancels a token and updates its owning process.</summary>
    public async ValueTask<ProcessSnapshot> CancelTokenAsync(
        SchemataProcessToken token,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var process = await persistence.FindAsync(services, $"processes/{token.Process}", ct);
        if (process is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = token.Process }
            );
        }

        var reg = ResolveRegistration(process.DefinitionName);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            var tokens = await LoadTokensAsync(scope, process.Name!, c);
            var ctx    = await CreateExecutionContextAsync(scope, process, principal, c);
            var target = tokens.FirstOrDefault(t => t.CanonicalName == token.CanonicalName);
            if (target is null) {
                throw new NotFoundException(
                    SchemataResources.PROCESS_TOKEN_NOT_FOUND,
                    new Dictionary<string, string?> {
                        ["token"] = token.CanonicalName, ["process"] = process.CanonicalName,
                    }
                );
            }

            if (TokenStates.IsTerminal(target.State)) {
                throw new FailedPreconditionException(
                    message: SchemataResources.GetResourceString(SchemataResources.PROCESS_TOKEN_NOT_READY),
                    reason: SchemataResources.PROCESS_TOKEN_NOT_READY);
            }

            var before   = WaitingMap(tokens);
            var previous = target.WaitingAtName ?? target.StateName;
            target.State         = "Cancelled";
            target.WaitingAtName = null;

            var transition = CancelTransition(process, target, previous, "Cancelled", "CancelToken", principal);

            if (tokens.All(t => TokenStates.IsTerminal(t.State))) {
                process.State = "Cancelled";
            }

            snapshot = new() { Process = process, Tokens = tokens, Transitions = [transition] };
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, before, c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await NotifyTransitionResultAsync(snapshot!, ct);
        return snapshot!;
    }

    private async ValueTask<SchemataProcess> StartCoreAsync<TState>(
        string               definitionName,
        StartProcessOptions? options,
        TState?              source,
        string?              sourceName,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) where TState : class {
        var reg    = ResolveRegistration(definitionName);
        var engine = ResolveEngine(reg);

        var process = NewProcess(definitionName, options);
        ProcessSnapshot? snapshot = null;

        await ExecuteWithNotificationAsync(process, async (scope, c) => {
            await BindStartSourceAsync(scope, reg, process, source, sourceName, c);
            var ctx = await CreateExecutionContextAsync(scope, process, principal, c);
            snapshot = await engine.StartAsync(reg.Definition, process, ctx, c);
            EnsureCatchesHaveHandlers(reg.Definition, snapshot);
            await RunAdvisorsAsync(reg, scope, ctx, snapshot, new Dictionary<string, string?>(), c);
            await persistence.PersistSnapshotAsync(scope, snapshot, c);
        }, ct);

        await notifier.NotifyStartedAsync(snapshot!, ct);
        await notifier.NotifyTransitionedAsync(snapshot!, ct);
        return process;
    }

    private async ValueTask ExecuteWithNotificationAsync(
        SchemataProcess                                   process,
        Func<FlowPersistenceScope, CancellationToken, Task> action,
        CancellationToken                                ct
    ) {
        try {
            await persistence.ExecuteAsync(services, action, ct);
        } catch (Exception ex) {
            await notifier.NotifyFailedAsync(process, ex, ct);
            throw;
        }
    }

    private async Task RunAdvisorsAsync(
        ProcessRegistration                 reg,
        FlowPersistenceScope                scope,
        FlowExecutionContext                execution,
        ProcessSnapshot                     snapshot,
        IReadOnlyDictionary<string, string?> before,
        CancellationToken                   ct
    ) {
        var tokens = snapshot.Transitions
                             .Select(transition => transition.Token)
                             .Where(token => !string.IsNullOrEmpty(token))
                             .Select(token => token!)
                             .Distinct(StringComparer.Ordinal);
        foreach (var tokenCanonical in tokens) {
            var token = snapshot.Tokens.FirstOrDefault(current => current.CanonicalName == tokenCanonical);
            if (token is null) {
                continue;
            }

            var context = new FlowTransitionContext {
                Definition            = reg.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = before.TryGetValue(tokenCanonical, out var waiting) ? waiting : null,
                UnitOfWork            = scope.UnitOfWork,
                Principal             = execution.Principal,
            };
            await RunSourceAdvisorsAsync(reg, scope, execution, context, ct);
        }

        await FlushTouchedSourcesAsync(scope, execution, snapshot.Process.CanonicalName ?? string.Empty, ct);

        var handlers = services.GetServices<IFlowCatchHandler>().ToList();

        foreach (var transition in snapshot.Transitions) {
            var token = snapshot.Tokens.FirstOrDefault(t => t.CanonicalName == transition.Token);
            if (token is null) {
                continue;
            }

            var context = new FlowTransitionContext {
                Definition            = reg.Definition,
                Snapshot              = snapshot,
                Token                 = TokenSnapshotFactory.From(token),
                PreviousWaitingAtName = transition.Token is not null && before.TryGetValue(transition.Token, out var waiting) ? waiting : null,
                UnitOfWork            = scope.UnitOfWork,
                Principal             = execution.Principal,
            };

            // Advice runs first and may reject the transition by throwing; a returned Block or
            // Handle only ends the chain. Arming then runs whatever the pipeline returned, because a
            // token parked on a catch nobody armed waits forever.
            await Advisor.For<IFlowTransitionAdvisor>().RunAsync(new AdviceContext(services), context, ct);

            // Every handler sees every transition: each one decides for itself which catches it just
            // gained and which it must release, so arming and releasing stay in one pass.
            foreach (var handler in handlers) {
                await handler.ArmAsync(context, ct);
            }
        }
    }

    /// <summary>
    ///     Fails a transition that would park a token on a catch no registered
    ///     <see cref="IFlowCatchHandler" /> delivers, so the run stops instead of waiting forever.
    /// </summary>
    private void EnsureCatchesHaveHandlers(ProcessDefinition definition, ProcessSnapshot snapshot) {
        var handlers = services.GetServices<IFlowCatchHandler>().ToList();
        var changed = snapshot.Transitions
                              .Select(transition => transition.Token)
                              .Where(token => !string.IsNullOrEmpty(token))
                              .Select(token => token!)
                              .Distinct(StringComparer.Ordinal);

        foreach (var tokenName in changed) {
            var token = snapshot.Tokens.FirstOrDefault(current => current.CanonicalName == tokenName);
            if (token is null) {
                continue;
            }

            foreach (var catchEvent in ResolveExternalCatches(definition, token)) {
                FlowCatchKind? kind = catchEvent.Definition switch {
                    Message         => FlowCatchKind.Message,
                    Signal          => FlowCatchKind.Signal,
                    TimerDefinition => FlowCatchKind.Timer,
                    _               => null,
                };

                if (kind is null || handlers.Any(handler => handler.Handles(kind.Value))) {
                    continue;
                }

                // The question is whether the catch has an owner, not which package supplies one.
                throw new FailedPreconditionException(
                    message: $"Flow catch '{catchEvent.Name}' waits on a {kind} event, but no registered "
                           + $"{nameof(IFlowCatchHandler)} delivers that kind; the token would wait forever.");
            }
        }
    }

    private static IEnumerable<FlowEvent> ResolveExternalCatches(
        ProcessDefinition      definition,
        SchemataProcessToken   token
    ) {
        if (!string.IsNullOrEmpty(token.WaitingAtName)) {
            var waiting = definition.AllElements.FirstOrDefault(element => element.Name == token.WaitingAtName);

            // Direct intermediate catches persist their own name as the waiting location.
            if (waiting is FlowEvent { Position: EventPosition.IntermediateCatch } catchEvent) {
                yield return catchEvent;
                yield break;
            }

            // Event-based gateways persist their name, while every outgoing catch is armed.
            if (waiting is EventBasedGateway gateway) {
                foreach (var flow in definition.AllFlows.Where(flow => flow.Source == gateway)) {
                    if (flow.Target is FlowEvent { Position: EventPosition.IntermediateCatch } outgoingCatch) {
                        yield return outgoingCatch;
                    }
                }
            }

            yield break;
        }

        if (!string.Equals(token.State, "Active", StringComparison.Ordinal)
         || definition.AllElements.FirstOrDefault(element => element.Name == token.StateName) is not Activity host) {
            yield break;
        }

        // Boundary catches leave the token active at their host, so attachment identifies the armed waits.
        foreach (var catchEvent in definition.AllElements.OfType<FlowEvent>()) {
            if (catchEvent.Position == EventPosition.Boundary && ReferenceEquals(catchEvent.AttachedTo, host)) {
                yield return catchEvent;
            }
        }
    }

    private async Task FlushTouchedSourcesAsync(
        FlowPersistenceScope  scope,
        FlowExecutionContext  execution,
        string                process,
        CancellationToken     ct
    ) {
        foreach (var ((sourceType, _), entity) in execution.TouchedSources) {
            var worker = SourceWorkers.GetOrAdd(sourceType, CreateSourceWorker);
            if (worker is not null) {
                await worker.FlushAsync(services, scope.UnitOfWork, entity, process, ct);
            }
        }

        execution.TouchedSources.Clear();
    }

    private async Task RunSourceAdvisorsAsync(
        ProcessRegistration  reg,
        FlowPersistenceScope scope,
        FlowExecutionContext execution,
        FlowTransitionContext context,
        CancellationToken    ct
    ) {
        var process = context.Snapshot.Process.CanonicalName;
        if (string.IsNullOrEmpty(process)) {
            return;
        }

        var token = context.Token.CanonicalName;
        var bindings = new List<SchemataProcessSource>();
        await foreach (var binding in scope.Sources.ListAsync<SchemataProcessSource>(
                           q => q.Where(s => s.Process == process && (s.Token == null || s.Token == token)), ct)) {
            bindings.Add(binding);
        }

        foreach (var binding in bindings) {
            if (!reg.SourceTypes.TryGetValue(binding.Name, out var descriptor)
             || FlowSourceTypeNames.ToName(descriptor.SourceType) != binding.SourceType) {
                continue;
            }

            var worker = SourceWorkers.GetOrAdd(descriptor.SourceType, CreateSourceWorker);
            if (worker is not null) {
                await worker.AdviseAsync(services, scope.UnitOfWork, execution, context, binding.Source, ct);
            }
        }
    }

    private async Task BindStartSourceAsync<TState>(
        FlowPersistenceScope scope,
        ProcessRegistration  reg,
        SchemataProcess      process,
        TState?              source,
        string?              sourceName,
        CancellationToken    ct
    ) where TState : class {
        if (source is null && string.IsNullOrEmpty(sourceName)) {
            return;
        }

        var (name, type, canonical, stamp) = StartSource(reg, source, sourceName);
        var row = new SchemataProcessSource {
            Process         = process.CanonicalName!,
            Token           = null,
            Name            = name,
            SourceType      = type,
            Source          = canonical,
            SourceTimestamp = stamp,
        };

        await scope.Sources.AddAsync(row, ct);
    }

    private static (string Name, string Type, string Canonical, Guid? Stamp) StartSource<TState>(
        ProcessRegistration reg,
        TState?             source,
        string?             sourceName
    ) where TState : class {
        if (source is ICanonicalName canonicalSource) {
            var type = typeof(TState);
            var candidates = reg.SourceTypes.Values.Where(descriptor => descriptor.SourceType == type).ToList();
            var conventional = FlowSourceDescriptor.DefaultBindingName(type);
            var binding = candidates.FirstOrDefault(descriptor => descriptor.BindingName == conventional);
            if (binding is null) {
                if (candidates.Count != 1) {
                    throw new FailedPreconditionException(
                        SchemataResources.PROCESS_SOURCE_BINDING_AMBIGUOUS,
                        new Dictionary<string, string?> { ["type"] = FlowSourceTypeNames.ToName(type) });
                }

                binding = candidates[0];
            }

            return (
                binding.BindingName,
                FlowSourceTypeNames.ToName(type),
                canonicalSource.CanonicalName!,
                source is IConcurrency concurrency ? concurrency.Timestamp : null);
        }

        var types = reg.SourceTypes.ToList();
        if (types.Count != 1) {
            throw new FailedPreconditionException(
                message: $"Process '{reg.Name}' binds {types.Count} source types; specify a source name.");
        }

        return (types[0].Key, FlowSourceTypeNames.ToName(types[0].Value.SourceType), sourceName!, null);
    }

    private static async ValueTask<string?> ResolveTargetAsync(
        IFlowRuntime                        engine,
        ProcessDefinition                   definition,
        SchemataProcess                     process,
        IReadOnlyList<SchemataProcessToken> tokens,
        FlowExecutionContext                context,
        IEventDefinition                    trigger,
        string?                             requested,
        CancellationToken                   ct
    ) {
        var targets = await engine.FindTriggerTargetsAsync(definition, process, tokens, context, trigger, ct);
        if (!string.IsNullOrEmpty(requested)) {
            if (!targets.Contains(requested, StringComparer.Ordinal)) {
                throw new FailedPreconditionException(
                    SchemataResources.PROCESS_TOKEN_NOT_READY,
                    new Dictionary<string, string?> { ["name"] = requested });
            }

            return requested;
        }

        if (targets.Count == 0) {
            throw new FailedPreconditionException(SchemataResources.PROCESS_TOKEN_NOT_READY);
        }

        if (targets.Count > 1) {
            throw new FailedPreconditionException(SchemataResources.PROCESS_TOKEN_NOT_READY);
        }

        return targets[0];
    }

    private static IReadOnlyList<string> FilterTargets(IReadOnlyList<string> targets, string? requested) {
        if (string.IsNullOrEmpty(requested)) {
            return targets;
        }

        return targets.Contains(requested, StringComparer.Ordinal) ? [requested] : [];
    }

    /// <summary>Deserializes an embedded message/signal payload using the framework's shared internal options.</summary>
    /// <remarks>
    ///     Embedded payloads are bound by CLR property name with case-insensitive matching via
    ///     <see cref="SchemataJson.Default" />; they deliberately do NOT follow the HTTP snake_case
    ///     wire policy configured by the transport JSON feature.
    /// </remarks>
    private static object? DeserializePayload(string? payload, Type? type) {
        if (string.IsNullOrEmpty(payload)) {
            return null;
        }

        // A payload whose message/signal declares no type cannot be bound to anything the engine
        // understands; deserializing into object would hand the engine an unusable value instead.
        if (type is null) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAYLOAD);
        }

        return JsonSerializer.Deserialize(payload, type, SchemataJson.Default);
    }

    private static Dictionary<string, string?> WaitingMap(IEnumerable<SchemataProcessToken> tokens) {
        return tokens.Where(t => !string.IsNullOrEmpty(t.CanonicalName))
                     .ToDictionary(t => t.CanonicalName!, t => t.WaitingAtName, StringComparer.Ordinal);
    }

    private static SchemataProcessTransition CancelTransition(
        SchemataProcess      process,
        SchemataProcessToken token,
        string?              previous,
        string               posterior,
        string               @event,
        ClaimsPrincipal?     principal
    ) {
        return new() {
            Name      = FlowHandlerSupport.NewLeafId(),
            Process   = process.Name,
            Token     = token.CanonicalName,
            Kind      = TransitionKind.Cancel,
            Previous  = previous,
            Posterior = posterior,
            Event     = @event,
            UpdatedBy = FlowHandlerSupport.ResolveUpdatedBy(principal),
        };
    }

    private static async ValueTask<IReadOnlyList<SchemataProcessToken>> LoadTokensAsync(
        FlowPersistenceScope scope,
        string               processName,
        CancellationToken    ct
    ) {
        var list = new List<SchemataProcessToken>();
        await foreach (var token in scope.Tokens.ListAsync<SchemataProcessToken>(q => q.Where(t => t.Process == processName), ct)) {
            list.Add(token);
        }

        return list;
    }

    private async ValueTask<FlowExecutionContext> CreateExecutionContextAsync(
        FlowPersistenceScope scope,
        SchemataProcess      process,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        var bindings = new List<ProcessCompensationBinding>();
        if (!string.IsNullOrEmpty(process.CanonicalName)) {
            await foreach (var binding in scope.Compensations.ListAsync<SchemataProcessCompensation>(
                               q => q.Where(row => row.Process == process.CanonicalName)
                                     .OrderBy(row => row.ScopeOwnerCanonicalName)
                                     .ThenBy(row => row.RegistrationOrder)
                                     .ThenBy(row => row.ActivityName), ct)) {
                bindings.Add(new(
                    binding.ScopeOwnerCanonicalName,
                    binding.ActivityName,
                    binding.RegistrationOrder));
            }
        }

        return new(scope.UnitOfWork, services) {
            LoadedCompensationBindings = bindings,
            Principal                  = principal,
            SourceReadGuard            = FlowSourceReadScope.Enter,
        };
    }

    private static SchemataProcess NewProcess(string definitionName, StartProcessOptions? options) {
        var leaf = FlowHandlerSupport.NewLeafId();
        return new() {
            Name           = leaf,
            CanonicalName  = $"processes/{leaf}",
            DefinitionName = definitionName,
            DisplayName    = string.IsNullOrWhiteSpace(options?.DisplayName) ? null : options.DisplayName,
            Description    = string.IsNullOrWhiteSpace(options?.Description) ? null : options.Description,
            IdempotencyKey = string.IsNullOrWhiteSpace(options?.IdempotencyKey) ? null : options.IdempotencyKey,
        };
    }

    private ProcessRegistration ResolveRegistration(string definitionName) {
        var registration = registry.GetRegistration(definitionName);
        if (registration is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = definitionName }
            );
        }

        return registration;
    }

    private IFlowRuntime ResolveEngine(ProcessRegistration reg) {
        var engine = services.GetKeyedService<IFlowRuntime>(reg.Engine);
        if (engine is null) {
            throw new FailedPreconditionException(
                SchemataResources.FLOW_RUNTIME_NOT_REGISTERED,
                new Dictionary<string, string?> { ["engine"] = reg.Engine }
            );
        }

        return engine;
    }

    private async ValueTask NotifyTransitionResultAsync(ProcessSnapshot snapshot, CancellationToken ct) {
        await notifier.NotifyTransitionedAsync(snapshot, ct);
        if (ProcessStates.IsTerminal(snapshot.Process.State)) {
            await notifier.NotifyTerminatedAsync(snapshot.Process, ct);
        }
    }

    private static ISourceWorker? CreateSourceWorker(Type type) {
        if (!typeof(ICanonicalName).IsAssignableFrom(type)) {
            return null;
        }

        var worker = Activator.CreateInstance(typeof(SourceWorker<>).MakeGenericType(type));
        return worker as ISourceWorker;
    }

    #region Nested type: ISourceWorker

    private interface ISourceWorker
    {
        Task AdviseAsync(
            IServiceProvider       services,
            IUnitOfWork            uow,
            FlowExecutionContext   execution,
            FlowTransitionContext  context,
            string                 source,
            CancellationToken      ct
        );

        Task FlushAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            object            entity,
            string            process,
            CancellationToken ct
        );
    }

    #endregion

    #region Nested type: SourceWorker

    private sealed class SourceWorker<TSource> : ISourceWorker
        where TSource : class, ICanonicalName
    {
        #region ISourceWorker Members

        public async Task AdviseAsync(
            IServiceProvider      services,
            IUnitOfWork           uow,
            FlowExecutionContext  execution,
            FlowTransitionContext context,
            string                source,
            CancellationToken     ct
        ) {
            var repository = services.GetRequiredService<IRepository<TSource>>();

            TSource? entity = null;
            if (execution.TouchedSources.TryGetValue((typeof(TSource), source), out var touched)) {
                entity = (TSource)touched;
            } else {
                repository.Join(uow);
                using (FlowSourceReadScope.Enter(repository)) {
                    entity = await repository.FirstOrDefaultAsync(q => q.Where(e => e.CanonicalName == source), ct);
                }
            }

            if (entity is null) {
                return;
            }

            var advice = new AdviceContext(services);
            advice.Set(new FlowSourceWriteBack(execution));
            await Advisor.For<IFlowSourceAdvisor<TSource>>().RunAsync(advice, context, entity, ct);
        }

        public async Task FlushAsync(
            IServiceProvider  services,
            IUnitOfWork       uow,
            object            entity,
            string            process,
            CancellationToken ct
        ) {
            var source = (TSource)entity;
            var sources = services.GetRequiredService<IRepository<TSource>>();
            var bindings = services.GetRequiredService<IRepository<SchemataProcessSource>>();
            sources.Join(uow);
            bindings.Join(uow);

            var canonical = source.CanonicalName;
            if (string.IsNullOrEmpty(canonical)) {
                return;
            }

            var type = FlowSourceTypeNames.ToName(typeof(TSource));
            var rows = new List<SchemataProcessSource>();
            await foreach (var row in bindings.ListAsync<SchemataProcessSource>(
                               q => q.Where(binding => binding.Process == process
                                                   && binding.Source == canonical
                                                   && binding.SourceType == type), ct)) {
                rows.Add(row);
            }

            if (source is IConcurrency concurrent) {
                foreach (var row in rows) {
                    if (row.SourceTimestamp is { } expected && concurrent.Timestamp != expected) {
                        throw new FailedPreconditionException(
                            SchemataResources.FLOW_SOURCE_MODIFIED_CONCURRENTLY,
                            new Dictionary<string, string?> { ["name"] = canonical });
                    }
                }
            }

            await sources.UpdateAsync(source, ct);

            if (source is IConcurrency) {
                TSource? persisted;
                using (FlowSourceReadScope.Enter(sources)) {
                    persisted = await sources.FirstOrDefaultAsync(q => q.Where(e => e.CanonicalName == canonical), ct);
                }

                if (persisted is not IConcurrency stamped) {
                    return;
                }

                foreach (var row in rows) {
                    row.SourceTimestamp = stamped.Timestamp;
                    await bindings.UpdateAsync(row, ct);
                }
            }
        }

        #endregion
    }

    #endregion
}
